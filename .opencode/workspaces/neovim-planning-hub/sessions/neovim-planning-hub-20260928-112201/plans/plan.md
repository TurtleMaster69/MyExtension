# Implementation Plan — Architecture Consolidation (5 lanes)

> **Lane: refactor (unit-only, e2e deferred).** Executes the whole-repo
> architecture-consolidation program from `docs/architecture-consolidation.md`
> (2026-09-28) — ~30 under-factored seams collapse into the target APIs, every
> extracted pure seam ships with NEW unit tests, and **no
> `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal changes** (M-M7 NOT
> triggered — the verification contract §8 forbids it). All e2e verification is
> **deferred to `e2e-queue.md`** (this machine cannot boot the VS Experimental
> Instance); the plan's gate is the two offline unit suites + the new seam tests.
>
> **Scope decision (user, 2026-09-28):** plan EVERYTHING (all 5 lanes) as one
> queue item; e2e deferred to a later date on a capable machine. The end-user
> keybindings UX items (`MyExtension:OpenKeybindings`, `ShowBindings`, surfacing
> unknown actions) are **deferred to feature-triage** — NOT in this plan.

**Goal:** Collapse the ~30 duplicated/under-factored seams identified in
`docs/architecture-consolidation.md` into the target APIs (one `NativeMethods`,
one `TelescopeLauncher`, one `ActionRegistry`, one `VsServices`, one
`WindowAdapter`, one pure `WindowNavigationEngine`, one `TextMotionEngine`, one
`FinderBase<THit>`, one log path), ship NEW unit tests for every extracted pure
seam (the `OverlayKeyHandler`/`TextMotionNavigator` pattern), keep every
diagnostic log literal byte-identical, and leave the repo behaviorally
unchanged — verified by the two offline unit suites staying green and the
deferred e2e suite.

**Approach:** Execute the doc's §7 lane order. Each lane is a self-contained,
independently verifiable unit executed top-to-bottom by the build-agent:

1. **Lane 1 — Mechanical dedup** (C1 → C6+L4 → C2 → C3 → C4 → L5 → L7 → L6 → X3)
2. **Lane 2 — Vim-motion / caret cluster** (T1 → T2 → T4 → T5 → T3)
3. **Lane 3 — Finder cluster** (L3 → L2 → L1 → C5 → C7)
4. **Lane 4 — Navigation cluster** (N2 → N4 → N3 → N1 → matrix-rebuild cache)
5. **Lane 5 — Cross-cutting** (X1 → X2 → C8 → X4)

**BP numbering:** BP numbers are **per-lane** (each lane section carries its own
BP-1..BP-N). The merge ids (C1, T1, L3, N2, X1, …) are globally unique and appear
in every BP step header, so cross-references are unambiguous. Each lane is
executed as a unit with its own gate.

**Baseline-relative file:line convention (MANDATORY — cross-lane safety):** All
`file:line` references in this plan are **baseline-relative** (pre-consolidation,
as of 2026-09-28). Earlier lanes delete/insert code, so a later lane's line
numbers drift. After each lane, the build-agent MUST re-locate every later-lane
reference **by symbol/member name** (e.g. `Debug.WriteLine` inside
`ToggleSolutionExplorer`, `Run_LogFileWriter_WritesAndClearsFile`), NOT by the
stale line number. The same applies to doc-ref steps: locate the backticked
token (e.g. `DefaultLogicalYWindowDivide`, `IVsUIWindowFrameExtractor`), not the
line number. This is the single highest-leverage guard against cross-lane
breakage.

**Doc-ref gate is DIFF-BASED (rev3 — the baseline lint is already red):**
`pwsh tools/check-doc-refs.ps1` currently exits 1 on **7 PRE-EXISTING unresolved
refs** that are NOT in this plan's scope — `FocusKeeper` ×2 +
`ArgumentOutOfRangeException` in `docs/progress.md:1252-1253,1278`; `SafeHandle`
×2 + 2 workspace-session paths in `.opencode/agent/code-review-hub.md`,
`.opencode/agent/code-review-worker.md`, `.opencode/agent/neovim-planning-hub.md`.
The doc-ref gate for this plan is therefore: **`git diff` on the touched docs must
NOT ADD any unresolved backticked token** (the plan's doc-ref steps reword the
symbols it deletes). Do NOT "fix" the 7 pre-existing refs (scope creep). Where a
Verify-with says "`pwsh tools/check-doc-refs.ps1` exits 0", read it as "the
doc-ref diff gate passes (no NEW unresolved refs)".

**Structural grounding (trailmark-recon, 2026-09-28):** all 25 claims verified
against the graph (1104 nodes, 494 proxies, 0 entrypoints). Six corrections to
the doc are folded into the lane sections (marked **CORRECTION**): (1) N3 — NO
switch statements in `WindowMatrix`; the 5 filter methods use if/else direction
branches; (2) C2 — 6 open-finder methods, not 7; (3) C3 — `ResolveAction` has 10
cases, not ~14; (4) C4 — ~18 `GetDTE` sites, not 12; (5) L5 — the prefix is
ALREADY centralized in `DiagnosticLog.Telescope` (46 `NeoVisualLog.Log` sites, not
31 hand-written literals); (6) X1 — ~51 `Debug.WriteLine` sites, not 27. Four
false-dead-code traps must NOT be reported as dead: `WindowMatrix.CheckDte`
(called from the ctor), `GeneralToolWindowController` ctor (new-ed in
`WindowManager.GetController`), `TextMotionHelper.TryMoveFocusedTextBox` (proxy →
`SolutionExplorerController`), `LinqExtensionMethods.DistinctBy` (proxy → tests
only).

---


---

# Lane 1 — Mechanical dedup (rev1, source-verified)

> **Lane 1 of the architecture-consolidation program** (`docs/architecture-consolidation.md`,
> 2026-09-28), executed top-to-bottom by the build-agent in the lane order
> **C1 → C6+L4 → C2 → C3 → C4 → L5 → L7 → L6 → X3** (BP-1..BP-9, one BP step per merge).
> Every merge is **behavior-preserving**; no new behavior.
>
> **Gate:** both offline unit suites stay green (`dotnet run --project tests/Telescope.Tests`
> → 56, `tests/NeoVisual.Tests` → 38); the new seam tests (BP-4, BP-6, BP-7, BP-8) are
> **RED before the merge and GREEN after**; `pwsh tools/check-doc-refs.ps1` exits 0 after the
> doc-ref update steps (BP-3, BP-8); `git diff` shows no change to any
> `[Telescope]`/`[NeoVisual]`/`[Hook]` **structured-log** literal (M-M7 NOT triggered).
> e2e is deferred to `e2e-queue.md` — **no e2e scenario name appears in any Verify-with**;
> the e2e gate column in the merges table is informational only.
>
> **Known-RED allowlist:** none — `docs/progress.md` reports no known-RED e2e scenario remains
> (all 35 green as of 2026-09-27); e2e is deferred anyway. One **documented deviation**
> (adjudicated, not a regression) is carried in the Verification Trace: BP-3 collapses 5
> `[NeoVisual]`-prefixed **Debug.WriteLine error-path** literals into one parameterized form
> (see BP-3 + KEY DECISIONS).
>
> **Hard requirements honored throughout:** net472 (no `IReadOnlySet<T>` — use
> `IReadOnlyCollection<Keys>`/`IReadOnlyDictionary`), `ThreadHelper.ThrowIfNotOnUIThread()` on
> every new VS-API method, LangVersion 14, Nullable enabled. No `[Telescope]`/`[NeoVisual]`/
> `[Hook]` log literal changes anywhere.

---

## 1. Merges table (current state verified against source, 2026-09-28)

| id | Merge | Current state (verified file:line) | Target design (exact API) | Blast radius | Coverage (unit tests) | e2e gate (deferred, informational) |
|----|-------|-----------------------------------|---------------------------|--------------|-----------------------|-------------------------------------|
| C1 | **NativeMethods** | 13 `DllImport` decls: `GlobalKeyboardHook.cs:257-277` (7: `SetWindowsHookEx`, `UnhookWindowsHookEx`, `CallNextHookEx`, `GetModuleHandle`, `GetAsyncKeyState`, `GetForegroundWindow`, `GetWindowThreadProcessId`) + `LowLevelKeyboardProc` delegate `:255`; `KeyInjection.cs:50-51` (`keybd_event`); `InputHandler.cs:606-608` (`GetWindowRectNative`) + `NativeRect` `:610-617` + private `GetWindowRect` helper `:596-604`; `MyExtensionPackage.cs:764-766` (`GetWindowRectNative`) + `NativeRect` `:768-775` + private `GetWindowRect` helper `:426-433`; `TextMotionHelper.cs:169-170` (`GetAsyncKeyState`); `TextInputToolWindowController.cs:336-337` (`GetFocus`) + `:339-340` (`GetAsyncKeyState`). `GetWindowRect`+`NativeRect` byte-identical in `InputHandler.cs` + `MyExtensionPackage.cs` | one `internal static class NativeMethods` (namespace `MyExtension`): `internal static System.Drawing.Rectangle? GetWindowRect(IntPtr hwnd)`, `keybd_event`, `SetWindowsHookEx`, `GetAsyncKeyState`, `GetForegroundWindow`, `GetWindowThreadProcessId`, `GetModuleHandle`, `CallNextHookEx`, `UnhookWindowsHookEx`, `GetFocus`, `GetWindowRectNative`, `NativeRect` struct, `LowLevelKeyboardProc` delegate; mechanical rename of ~13 call sites | hook + InputHandler + package + both motion surfaces | none (mechanical) | hook install + navigation + telescope-open |
| C6 | **OpenFileAtLine** | `OpenReference` `MyExtensionPackage.cs:445-458` + `OpenImplementation` `:466-479` byte-identical (both: `File.Exists` guard → `GetDTE` → `ItemOperations.OpenFile` → `TextSelection.GotoLine`) | `private void OpenFileAtLine(string path, int line)` in `MyExtensionPackage`; both `OnSelected` wrappers call it. **Do with L4.** | MyExtensionPackage | none | telescope-references/implementation |
| L4 | **DteFileOpener** | `GotoLine` byte-identical in `CodeIssuesFinder.cs:122-137` + `GrepFinder.cs:136-151`; References/Implementation host `_opener`s (`MyExtensionPackage.cs:445-458,466-479`) re-implement the same | `internal static class DteFileOpener { public static void OpenAtLine(EnvDTE.DTE dte, string path, int line) }` in the Telescope project (visible to `MyExtension` via the existing `<InternalsVisibleTo Include="MyExtension" />` at `Telescope.csproj:32`); all four call it | CodeIssuesFinder, GrepFinder, MyExtensionPackage | none (VS-coupled) | telescope-issues/grep/references/implementation |
| C2 | **TelescopeLauncher** | 6 near-identical open-finder methods — `InputHandler.cs:465-479` (`OpenTelescope`), `:486-500` (`OpenTelescopeIssues`), `:508-522` (`OpenTelescopeReferences`), `:530-544` (`OpenTelescopeImplementation`), `:551-565` (`OpenTelescopeGrep`) + `MyExtensionPackage.cs:410-424` (`OpenTelescope`) — each re-resolves DTE + `GetWindowRect` | `internal sealed class TelescopeLauncher { public void Open(string finderName) }` = GetDTE + `NativeMethods.GetWindowRect` + `_telescope.Open(name, rect, hwnd)`; `internal static readonly IReadOnlyDictionary<string,string> FinderNames` (OrdinalIgnoreCase); `ResolveAction` maps `"telescope-issues" → () => launcher.Open(FinderNames["telescope-issues"])` via the dictionary; package command → `launcher.Open("Files")`; 6 methods deleted | InputHandler.ResolveAction + package command | none | all telescope-* |
| C3 | **ActionRegistry** | `ResolveAction` switch, 10 cases at `InputHandler.cs:180-194` (method `:175-208`); names must stay in sync with `default-keybindings.json` by hand (10 built-ins verified: navigate-left/right/up/down, telescope, telescope-issues, telescope-references, telescope-implementation, telescope-grep, toggle-solution-explorer) | `internal static class Actions` (namespace `MyExtension`) with `internal static readonly IReadOnlyDictionary<string, Func<InputHandler, TelescopeLauncher, Action>> Registry` (OrdinalIgnoreCase, 10 entries) and `internal static Action? Resolve(string lowerName, InputHandler handler, TelescopeLauncher launcher)`; `ResolveAction = Registry.TryGetValue(lower, out f) ? f(this, _launcher) : ParseCommand(lower)` | InputHandler only | **NEW** `Run_ActionsRegistry_*` (RED: `Actions` doesn't exist) | neovisual-leader + KeybindingConfig units |
| C4 | **VsServices** | 5 `SComponentModel` blocks: `InputHandler.cs:124-126`, `MyExtensionPackage.cs:498-500`, `:580-582`, `:688-689` (all `IServiceProvider`-based) + `VimModeTracker.cs:523` (`Package.GetGlobalService` variant); **18 verified `GetDTE` sites** — `InputHandler.cs:221,471,492,514,536,557,577`; `MyExtensionPackage.cs:71,72,73,85,416,452,473,491,573`; `WindowMatrix.cs:58,123`; `UtilityMethods.cs:17-22` uncached | `internal static class VsServices` (namespace `MyExtension`) with per-package caching: `public static IComponentModel? ComponentModel(IServiceProvider sp)`, `public static T? Mef<T>(IServiceProvider sp) where T : class`, `public static DTE Dte(IServiceProvider sp)`, `public static IComponentModel? GlobalComponentModel()` | InputHandler, VimModeTracker, MyExtensionPackage, WindowMatrix | none | references/implementation e2e |
| L5 | **TelescopeLog** | prefix ALREADY centralized in `DiagnosticLog.Telescope` (`DiagnosticLog.cs:12`); **46 verified `NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}...` sites across 6 files** — `CodeIssuesFinder.cs` (3: 112,116,130), `FileFinder.cs` (3: 94,106,111), `GrepFinder.cs` (5: 70,96,126,130,144), `ImplementationFinder.cs` (3: 61,83,87), `ReferencesFinder.cs` (3: 62,84,88), `TelescopeOverlay.cs` (29) | `internal static class TelescopeLog { public static void Log(string message) => NeoVisualLog.Log(DiagnosticLog.Telescope + message); }` — one line per diagnostic, prefix guaranteed | all Telescope log call sites | **NEW** `Run_TelescopeLog_*` (RED: `TelescopeLog` doesn't exist) | e2e (log lines are the contract) |
| L7 | **CaretPlacement** | magic ints 0/1/2: `OverlayKeyHandler.EnterInsertMode(int)` `:176-189` (`HandleNormal` I/A cases `:164-167`); `TelescopeOverlay.ApplyInsertCaret(int)` `:571-585` + 3 `ApplyAction` insert branches `:891-911` | `internal enum CaretPlacement { Current, End, Start }`; `OverlayKeyHandler.EnterInsertMode(CaretPlacement)`; `TelescopeOverlay.EnterInsert(CaretPlacement)` + `ApplyInsertCaret(CaretPlacement)` (delegate to `TextMotionNavigator` insert methods) | OverlayKeyHandler + overlay | **NEW** `Run_CaretPlacement_*` (RED: enum doesn't exist) | telescope-prompt-motions + units |
| L6 | **IFinder query fold** | `IQueryFinder` forces a no-arg `GetCandidates()` stub (`IQueryFinder.cs:11-14`, `GrepFinder.cs:47-50`) + `is IQueryFinder` branch (`TelescopeOverlay.cs:341`, `RefreshQueryDrivenAsync` `:359-371`); initial gather at `ShowOverlay` `:255` | `IReadOnlyList<FinderEntry> GetCandidates(string query = "")` on `IFinder` (`TelescopeFinder.cs:23`) + `bool IsQueryDriven { get; }`; delete `IQueryFinder` + the `is` check | GrepFinder + overlay | **NEW** `Run_GetCandidates_DefaultQuery_*` (RED: signature change) | telescope-grep e2e |
| X3 | **TestRunner** | reflection runner + `Assert` byte-identical in both test `Program.cs` (`tests/Telescope.Tests/Program.cs:26-85` + `Assert` `:950-975`; `tests/NeoVisual.Tests/Program.cs:29-88` + `Assert` `:499-524`) | one shared `tests/TestRunner.cs` (`namespace TestHarness`) via `<Compile Include="..\TestRunner.cs" Link="TestRunner.cs" />` in both csproj | both test projects | none (dedup) | unit suites |

**Lane 1 acceptance:** both unit suites stay green; `git diff` shows no change to any
`[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal; the new seam tests (C3, L5, L7, L6)
are RED before the merge and GREEN after.

---

## 2. Build Plan — Lane 1 (BP-1..BP-9)

## BP-1 — C1: NativeMethods (one Win32 P/Invoke surface)

- **Files:**
  - CREATE `MyExtension/NativeMethods.cs`
  - MODIFY `MyExtension/GlobalKeyboardHook.cs` (delete the 7 `DllImport` decls `:257-277` + `LowLevelKeyboardProc` delegate `:255`; update call sites `:88,93,109,114-116,149,201-202,217,220,242`)
  - MODIFY `MyExtension/KeyInjection.cs` (delete `keybd_event` decl `:50-51`; update call sites `:46-47`)
  - MODIFY `MyExtension/InputHandler.cs` (delete the private `GetWindowRect` helper `:596-604` + `GetWindowRectNative` `:606-608` + `NativeRect` `:610-617`; update call sites `:472,493,515,537,558`)
  - MODIFY `MyExtension/MyExtensionPackage.cs` (delete the private `GetWindowRect` helper `:426-433` + `GetWindowRectNative` `:764-766` + `NativeRect` `:768-775`; update call site `:417`)
  - MODIFY `MyExtension/ToolWindows/TextMotionHelper.cs` (delete `GetAsyncKeyState` decl `:169-170`; update call site `:61`)
  - MODIFY `MyExtension/ToolWindows/TextInputToolWindowController.cs` (delete `GetFocus` `:336-337` + `GetAsyncKeyState` `:339-340` decls; update call sites `:119,166,260`)
- **Change:** one `internal static class NativeMethods` (namespace `MyExtension`) holding all 13 `DllImport` decls — `SetWindowsHookEx`, `UnhookWindowsHookEx`, `CallNextHookEx`, `GetModuleHandle`, `GetAsyncKeyState`, `GetForegroundWindow`, `GetWindowThreadProcessId`, `keybd_event`, `GetFocus`, `GetWindowRectNative` — plus the `LowLevelKeyboardProc` delegate (moved from `GlobalKeyboardHook`; `GlobalKeyboardHook` keeps a `LowLevelKeyboardProc` field so the GC can't collect it), the `NativeRect` struct, and a convenience `internal static System.Drawing.Rectangle? GetWindowRect(IntPtr hwnd)` (the byte-identical helper currently duplicated in `InputHandler` + `MyExtensionPackage`). Mechanical rename of ~13 call sites to `NativeMethods.X`; the two private `GetWindowRect` helpers and the two `NativeRect` structs are deleted. No signature/behavior change.
- **Verify-with:** `dotnet build` succeeds; `dotnet run --project tests/Telescope.Tests` (56) and `tests/NeoVisual.Tests` (38) stay green; `git diff` shows no `[Telescope]`/`[NeoVisual]`/`[Hook]` literal change. No new unit tests (mechanical).
- **Fails-if:** compile error `NativeMethods`/`GetWindowRectNative`/`NativeRect` not found; a call site still references a deleted private decl; `NativeMethods.GetWindowRect` returns a different rect than the old helper (behavior drift).

## BP-2 — C6+L4: OpenFileAtLine + DteFileOpener (one open-file-at-line)

- **Files:**
  - CREATE `Telescope/DteFileOpener.cs`
  - MODIFY `Telescope/CodeIssuesFinder.cs` (delete `GotoLine` `:122-137`; `OnSelected` `:111` calls `DteFileOpener.OpenAtLine`)
  - MODIFY `Telescope/GrepFinder.cs` (delete `GotoLine` `:136-151`; `OnSelected` `:125` calls `DteFileOpener.OpenAtLine`)
  - MODIFY `MyExtension/MyExtensionPackage.cs` (`OpenReference` `:445-458` + `OpenImplementation` `:466-479` become thin wrappers; add `private void OpenFileAtLine(string path, int line)`)
- **Change:** `internal static class DteFileOpener { public static void OpenAtLine(EnvDTE.DTE dte, string path, int line) }` in the Telescope project (visible to `MyExtension` via the existing `<InternalsVisibleTo Include="MyExtension" />` at `Telescope.csproj:32`). `OpenAtLine` = `dte.ItemOperations.OpenFile(path); if (dte.ActiveDocument?.Selection is TextSelection sel && line > 0) { sel.GotoLine(line, false); NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}goto line={line}"); }` — the byte-identical `GotoLine` body, preserving the `goto line=` diagnostic. `MyExtensionPackage.OpenFileAtLine(string path, int line)` = `DteFileOpener.OpenAtLine(CardinalNavigation.UtilityMethods.GetDTE(this), path, line)` (the `GetDTE` call is switched to `VsServices.Dte` in BP-5). `OpenReference`/`OpenImplementation` become `OpenFileAtLine(hit.FilePath, hit.LineNumber)`. The `File.Exists` guards stay in the callers (unchanged).
- **Verify-with:** `dotnet build`; both unit suites stay green. The `[Telescope] goto line={line}` diagnostic is preserved byte-identical for issues/grep and is now also emitted by references/implementation (a NEW line for those two — no literal changed, no asserted line removed; M-M7 not triggered). No new unit tests (VS-coupled).
- **Fails-if:** `[Telescope] goto line=...` no longer emitted for issues/grep; a reference/implementation open no longer jumps to the line; `DteFileOpener` not visible from `MyExtension` (InternalsVisibleTo regression).

## BP-3 — C2: TelescopeLauncher (one open-finder entry point)

- **Files:**
  - CREATE `MyExtension/TelescopeLauncher.cs`
  - MODIFY `MyExtension/InputHandler.cs` (delete `OpenTelescope` `:465-479`, `OpenTelescopeIssues` `:486-500`, `OpenTelescopeReferences` `:508-522`, `OpenTelescopeImplementation` `:530-544`, `OpenTelescopeGrep` `:551-565`; add `_launcher` field; `ResolveAction` telescope cases → `_launcher.Open(...)`)
  - MODIFY `MyExtension/MyExtensionPackage.cs` (`OpenTelescope` `:410-424` → `_launcher.Open("Files")`; command registration `:406` unchanged shape)
  - MODIFY docs (doc-ref, required — `OpenTelescope` becomes un-greppable): `docs/progress.md` (`:632,659,683,1200,1202`) and `docs/architecture-review.md` (`:285`) — remove the backticks / reword the `OpenTelescope*` references so `pwsh tools/check-doc-refs.ps1` passes. (Verified: `docs/code-review.md` also mentions `OpenTelescope*` but is NOT in the lint's scanned doc set — no change needed there.)
- **Change:** `internal sealed class TelescopeLauncher { private readonly AsyncPackage _package; private readonly TelescopeController _telescope; public void Open(string finderName) }` = `ThreadHelper.ThrowIfNotOnUIThread(); try { var dte = CardinalNavigation.UtilityMethods.GetDTE(_package); var centerRect = NativeMethods.GetWindowRect(dte.MainWindow.HWnd); _telescope.Open(finderName, centerRect, dte.MainWindow.HWnd); } catch (Exception ex) { Debug.WriteLine($"{Telescope.DiagnosticLog.NeoVisual}Failed to open Telescope {finderName}: {ex.Message}"); }`. (`TelescopeController.Open(string finderName, System.Drawing.Rectangle? centerRect = null, IntPtr ownerHwnd = default)` at `TelescopeController.cs:50`.) Add `internal static readonly IReadOnlyDictionary<string,string> FinderNames` (OrdinalIgnoreCase): `telescope→Files`, `telescope-issues→Issues`, `telescope-references→References`, `telescope-implementation→Implementation`, `telescope-grep→Grep`.   `InputHandler.ResolveAction` telescope cases → `() => _launcher.Open(TelescopeLauncher.FinderNames["telescope-issues"])` etc.; package command → `_launcher.Open("Files")`. 6 methods deleted. **Documented deviation:** the 5 distinct `[NeoVisual]Failed to open Telescope ...` Debug.WriteLine error-path literals (inside the deleted open-finder methods) **plus the 6th `[MyExtension]Failed to open Telescope: {ex}` Debug.WriteLine at `MyExtensionPackage.cs:422`** (inside the deleted `OpenTelescope` `:410-424`) collapse into the one parameterized `[NeoVisual]Failed to open Telescope {finderName}` form above — failure-path only, never asserted by the harness (grep-verified: no `Failed to open Telescope` in `tools/`), not in the structured exp log. `[MyExtension]` is outside the M-M7 contract set, so this is not a contract violation.
- **Verify-with:** `dotnet build`; both unit suites stay green; `pwsh tools/check-doc-refs.ps1` exits 0 (after the doc-ref update). Diagnostics unchanged: `[NeoVisual] leader-binding executed: ...` / `[NeoVisual] shortcut-binding executed: ...`. No new unit tests (VS-coupled).
- **Fails-if:** `[NeoVisual] leader-binding executed: F,D` no longer fires (telescope binding broken); `_telescope.Open` called with the wrong finder name; doc-ref lint fails on `OpenTelescope`.

## BP-4 — C3: ActionRegistry (ResolveAction via a registry)

- **Files:**
  - CREATE `MyExtension/Actions.cs`
  - MODIFY `MyExtension/InputHandler.cs` (`ResolveAction` `:175-208` → registry lookup + `ParseCommand` fallback; make `Navigate` and `ToggleSolutionExplorer` `internal`)
  - MODIFY `tests/NeoVisual.Tests/Program.cs` (add `Run_ActionsRegistry_*` tests)
- **Change:** `internal static class Actions` (namespace `MyExtension`, `using CardinalNavigation;` for `CardinalNavigationConstants`) with `internal static readonly IReadOnlyDictionary<string, Func<InputHandler, TelescopeLauncher, Action>> Registry` (OrdinalIgnoreCase, 10 entries) and `internal static Action? Resolve(string lowerName, InputHandler handler, TelescopeLauncher launcher) => Registry.TryGetValue(lowerName, out var f) ? f(handler, launcher) : null;`. The 10 entries: `navigate-left/right/up/down` → `() => handler.Navigate(CardinalNavigationConstants.LEFT/RIGHT/UP/DOWN)`; `telescope` → `() => launcher.Open(TelescopeLauncher.FinderNames["telescope"])`; `telescope-issues/references/implementation/grep` likewise; `toggle-solution-explorer` → `() => handler.ToggleSolutionExplorer()`. `InputHandler.ResolveAction` becomes: `var action = Actions.Resolve(lower, this, _launcher); if (action != null) return action; return ParseCommand(trimmed);` where `ParseCommand` is the existing `command:`-prefix handling extracted from `:196-205`. `Navigate`/`ToggleSolutionExplorer` become `internal` (the registry factories are compiled in `Actions`). **Deviation from the plan-table sketch:** the registry stores `Func<InputHandler, TelescopeLauncher, Action>` factories, not plain `Action` — the delegates capture the handler/launcher instances, so the registry can be a static field and stay hermetically testable.
- **Verify-with:** NEW tests in `tests/NeoVisual.Tests` (RED: `Actions` doesn't exist → compile error): `Run_ActionsRegistry_ContainsAllBuiltins` (`Registry.Count == 10`, all 10 names present), `Run_ActionsRegistry_CaseInsensitive` (`Actions.Resolve("NAVIGATE-LEFT", null!, null!)` non-null), `Run_ActionsRegistry_UnknownFallsThrough` (`Actions.Resolve("command:File.Save", null!, null!)` null; `Actions.Resolve("bogus", null!, null!)` null), `Run_ActionsRegistry_TelescopeMapsToFinder` (`TelescopeLauncher.FinderNames["telescope-issues"] == "Issues"`, all 5). Diagnostic unchanged: `[NeoVisual] leader-binding executed: ...`.
- **Fails-if:** `Run_ActionsRegistry_*` fails; a `default-keybindings.json` action name no longer resolves (e.g. `toggle-solution-explorer`); `ResolveAction` returns null for a built-in name.

## BP-5 — C4: VsServices (one MEF/DTE resolver)

- **Files:**
  - CREATE `MyExtension/VsServices.cs`
  - MODIFY `MyExtension/CardinalMovment/UtilityMethods.cs` (delete `GetDTE` `:17-22`)
  - MODIFY `MyExtension/TelescopeLauncher.cs` (**created in BP-3** — its `GetDTE(_package)` call → `VsServices.Dte(_package)`; without this the Lane 1 build gate fails after `GetDTE` is deleted)
  - MODIFY `MyExtension/InputHandler.cs` (`:124-126` → `VsServices.Mef<VimModeTracker>`; the surviving `GetDTE` sites `:221,577` → `VsServices.Dte` — the 5 sites inside the open-finder methods were deleted in BP-3)
  - MODIFY `MyExtension/MyExtensionPackage.cs` (`:498-500,580-582,688-689` → `VsServices.Mef<T>`; the surviving `GetDTE` sites → `VsServices.Dte`, incl. the `OpenFileAtLine` site created in BP-2; the sites inside `OpenTelescope`/`OpenReference`/`OpenImplementation` were deleted/rewritten in BP-2/BP-3)
  - MODIFY `MyExtension/VimModeTracker.cs` (`:523` → `VsServices.GlobalComponentModel()`)
  - MODIFY `MyExtension/CardinalMovment/WindowMatrix.cs` (`:58,123` → `VsServices.Dte`)
- **Change:** `internal static class VsServices` (namespace `MyExtension`) with per-package caching (a `ConditionalWeakTable<object, object>` keyed by the `IServiceProvider`): `public static IComponentModel? ComponentModel(IServiceProvider sp)` (`ThreadHelper.ThrowIfNotOnUIThread(); return sp.GetService(typeof(SComponentModel)) as IComponentModel;`), `public static T? Mef<T>(IServiceProvider sp) where T : class` (`ComponentModel(sp)?.DefaultExportProvider.GetExportedValue<T>()`), `public static DTE Dte(IServiceProvider sp)` (`(DTE)sp.GetService(typeof(DTE))`, cached), `public static IComponentModel? GlobalComponentModel()` (`Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel`). Update the **surviving `GetDTE` sites** (rev3: the baseline "18 sites" is stale after BP-2/BP-3 — the sites inside `OpenTelescope`/`OpenReference`/`OpenImplementation` were deleted/rewritten; enumerate by symbol, not count): `InputHandler` `:221,577`; `MyExtensionPackage` `:71,72,73,85,491,573` + the `OpenFileAtLine` site created in BP-2; `WindowMatrix` `:58,123`; `TelescopeLauncher` (created in BP-3). Plus the 5 `SComponentModel` blocks (4 `IServiceProvider`-based + `VimModeTracker.GetComponentModel`). Delete `UtilityMethods.GetDTE` (the rest of `UtilityMethods` stays — Lane 4 owns it). **Risk note:** `Mef<T>` uses `DefaultExportProvider.GetExportedValue<T>`; verify `VisualStudioWorkspace` and `IVsEditorAdaptersFactoryService` resolve identically to the current `componentModel.GetService<T>()` (both are MEF-exported parts — expected equivalent).
- **Verify-with:** `dotnet build`; both unit suites stay green. No new unit tests (VS-coupled). Diagnostics unchanged (`[NeoVisual] vim-mode=...`, `[Telescope] references gathered ...`, etc.).
- **Fails-if:** `VsServices.Dte` returns null/throws at a call site; `Mef<VisualStudioWorkspace>` returns null where the old code resolved it; `VimModeTracker` MEF resolution breaks (falls back to `new VimModeTracker()` — VsVim mode-awareness silently lost).

## BP-6 — L5: TelescopeLog (one-line prefix helper)

- **Files:**
  - CREATE `Telescope/TelescopeLog.cs`
  - MODIFY `Telescope/CodeIssuesFinder.cs`, `Telescope/FileFinder.cs`, `Telescope/GrepFinder.cs`, `Telescope/ImplementationFinder.cs`, `Telescope/ReferencesFinder.cs`, `Telescope/TelescopeOverlay.cs` (46 `NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}...` → `TelescopeLog.Log($"...")`)
  - MODIFY `tests/Telescope.Tests/Program.cs` (add `Run_TelescopeLog_*` tests)
- **Change:** `internal static class TelescopeLog { public static void Log(string message) => NeoVisualLog.Log(DiagnosticLog.Telescope + message); }`. Replace the **46 verified `NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}...` call sites** across the 6 files above (CodeIssuesFinder 3, FileFinder 3, GrepFinder 5, ImplementationFinder 3, ReferencesFinder 3, TelescopeOverlay 29). **Correction to the plan table:** the scope is 46 `NeoVisualLog.Log` call sites across 6 files, not 63 across ~8 — the other 17 `DiagnosticLog.Telescope` usages are `Debug.WriteLine` error paths (not `NeoVisualLog.Log`), which are NOT `TelescopeLog` targets (X1 in Lane 5 owns those). The emitted text is byte-identical (the prefix is already centralized in `DiagnosticLog.Telescope` at `DiagnosticLog.cs:12`); this removes the concatenation boilerplate only.
- **Verify-with:** NEW tests in `tests/Telescope.Tests` (RED: `TelescopeLog` doesn't exist → compile error): `Run_TelescopeLog_Prefix` (point `LogFileWriter.LogPath` at a temp file, `TelescopeLog.Log("hello")`, assert the file contains `[Telescope] hello` — mirrors `Run_LogFileWriter_WritesAndClearsFile`), `Run_TelescopeLog_EmptyMessage` (`TelescopeLog.Log("")` emits `[Telescope] `). Extends `Run_LogPrefixes_Pinned`. Diagnostic: every `[Telescope] ...` line byte-identical.
- **Fails-if:** `Run_TelescopeLog_*` fails; any `[Telescope]` literal changes (`git diff`); a call site still hand-writes the prefix.

## BP-7 — L7: CaretPlacement (shared insert-caret enum)

- **Files:**
  - MODIFY `Telescope/OverlayKeyHandler.cs` (add `CaretPlacement` enum; `EnterInsertMode(int)` `:176-189` → `EnterInsertMode(CaretPlacement)`; `HandleNormal` `:164-167` I/A cases)
  - MODIFY `Telescope/TelescopeOverlay.cs` (collapse the 3 `ApplyAction` insert branches `:891-911` into one `EnterInsert(CaretPlacement)`; `ApplyInsertCaret(int)` `:571-585` → `ApplyInsertCaret(CaretPlacement)`)
  - MODIFY `tests/Telescope.Tests/Program.cs` (add `Run_CaretPlacement_*` tests)
- **Change:** `internal enum CaretPlacement { Current, End, Start }` (in `OverlayKeyHandler.cs` or a new `Telescope/CaretPlacement.cs`). `OverlayKeyHandler.EnterInsertMode(CaretPlacement)`: `End→OverlayAction.EnterInsertAppend`, `Start→OverlayAction.EnterInsertStart`, `Current→OverlayAction.EnterInsert`. `HandleNormal`: `I→EnterInsertMode(CaretPlacement.Current)`, `A→EnterInsertMode(CaretPlacement.End)`. `TelescopeOverlay`: collapse the 3 insert branches into one `EnterInsert(CaretPlacement placement)` = `_promptBox.IsReadOnly = false; UpdateModeLabel(); FocusPrompt(); ApplyInsertCaret(placement);`. `ApplyInsertCaret(CaretPlacement)` delegates to `TextMotionNavigator`: `End→InsertEnd()`, `Start→InsertStart()`, `Current→` no motion (caret clamped to current, matching the existing default). **Preserve behavior:** the overlay's "a" (append) maps to `InsertEnd` (end of text), NOT `InsertAfter` (caret+1) — do not "fix" this.
- **Verify-with:** NEW tests in `tests/Telescope.Tests` (RED: `CaretPlacement` doesn't exist → compile error): `Run_CaretPlacement_EnterInsertMapsToActions` (`I→EnterInsert`, `A→EnterInsertAppend`; `EnterInsertMode(End)→EnterInsertAppend`, `(Start)→EnterInsertStart`, `(Current)→EnterInsert`), `Run_CaretPlacement_EnumValues` (the 3 members exist). Diagnostic unchanged: `[Telescope] key=... mode=... handled=...`.
- **Fails-if:** `Run_CaretPlacement_*` fails; i/a/I caret placement changes (i stays at current, a at end, I at start); the `key=... mode=... handled=...` log line changes.

## BP-8 — L6: IFinder query fold (delete IQueryFinder)

- **Files:**
  - MODIFY `Telescope/TelescopeFinder.cs` (`IFinder.GetCandidates()` `:23` → `IReadOnlyList<FinderEntry> GetCandidates(string query = "")`; add `bool IsQueryDriven { get; }`)
  - MODIFY `Telescope/GrepFinder.cs` (delete the no-arg `GetCandidates()` stub `:47-50`; `IsQueryDriven => true`)
  - MODIFY `Telescope/FileFinder.cs`, `Telescope/CodeIssuesFinder.cs`, `Telescope/ReferencesFinder.cs`, `Telescope/ImplementationFinder.cs` (`IsQueryDriven => false`)
  - MODIFY `Telescope/TelescopeOverlay.cs` (`:341` `is IQueryFinder` → `_activeFinder.IsQueryDriven`; `RefreshQueryDrivenAsync(IFinder, ...)` `:359-371`)
  - DELETE `Telescope/IQueryFinder.cs`
  - MODIFY `tests/Telescope.Tests/Program.cs` (add `Run_GetCandidates_DefaultQuery_*` tests)
  - MODIFY docs (doc-ref, required — `IQueryFinder` + `IQueryFinder.cs` become un-greppable): `AGENTS.md` (`:256`), `.opencode/skills/vs-extension-dev/SKILL.md` (`:70`), `docs/spec.md` (`:318`), `docs/progress.md` (`:644,656,665,955`), `.opencode/agent/code-review-hub.md` (`:262,353`), `.opencode/agent/neovim_review_hub.md` (`:123,135`) — remove the backticks / reword the `IQueryFinder` and `IQueryFinder.cs` references so `pwsh tools/check-doc-refs.ps1` passes. (Verified: `docs/progress.md:665` mentions `IQueryFinder` in plain text, not backticked — it will not fail the lint, but reword it anyway for hygiene; `docs/architecture-consolidation.md` + `docs/code-review.md` also mention it but are NOT in the lint's scanned doc set.)
- **Change:** fold the query into `IFinder`: `IReadOnlyList<FinderEntry> GetCandidates(string query = "")` (the no-arg call still works for the initial gather at `TelescopeOverlay.ShowOverlay` `:255`). Add `bool IsQueryDriven { get; }` to `IFinder` — **required replacement** for the deleted `is IQueryFinder` check (without it the overlay cannot distinguish query-driven finders, and always re-gathering would be a behavior/perf regression). `GrepFinder`: delete the no-arg stub, `IsQueryDriven => true`; the other 4 finders: `IsQueryDriven => false`. Overlay `RefreshResults`: `if (_activeFinder.IsQueryDriven)` → `RefreshQueryDrivenAsync`. Delete `IQueryFinder.cs`.
- **Verify-with:** NEW tests in `tests/Telescope.Tests` (RED: `GetCandidates(string)` signature change — `finder.GetCandidates("")` does not compile before the fold): `Run_GetCandidates_DefaultQuery_MatchesNoArg` (hermetic `FileFinder`: `GetCandidates()` equals `GetCandidates("")`), `Run_GetCandidates_DefaultQuery_GrepEmpty` (`GrepFinder.GetCandidates()` == `GetCandidates("")` == empty), `Run_GetCandidates_IsQueryDriven` (`GrepFinder` true, `FileFinder` false). Diagnostic unchanged: `[Telescope] grep hits=...`. `pwsh tools/check-doc-refs.ps1` exits 0 (after the doc-ref update).
- **Fails-if:** `Run_GetCandidates_DefaultQuery_*` fails; the overlay re-gathers fzf finders per keystroke (behavior regression); `[Telescope] grep hits=...` changes; doc-ref lint fails on `IQueryFinder`.

## BP-9 — X3: TestRunner (one shared runner + Assert)

- **Files:**
  - CREATE `tests/TestRunner.cs`
  - MODIFY `tests/Telescope.Tests/Program.cs` (replace the reflection runner `:26-85` + `Assert` `:950-975` with a thin `Main`; add `using TestHarness;`; keep the `Tests` class)
  - MODIFY `tests/NeoVisual.Tests/Program.cs` (same; runner `:29-88` + `Assert` `:499-524`)
  - MODIFY `tests/Telescope.Tests/Telescope.Tests.csproj` + `tests/NeoVisual.Tests/NeoVisual.Tests.csproj` (add `<Compile Include="..\TestRunner.cs" Link="TestRunner.cs" />`)
- **Change:** one shared `tests/TestRunner.cs` in `namespace TestHarness` containing `internal static class TestRunner { public static int Run(Type testsType, string[] args) }` (the byte-identical reflection runner from both `Program.cs`, parameterized by the project's `Tests` type — discovery, `--list`, substring filter, pass/fail tally, exit code) + `internal static class Assert { Equal/True/False }`. Each project's `Program.cs` shrinks to `private static int Main(string[] args) => TestHarness.TestRunner.Run(typeof(Tests), args);` + `using TestHarness;` (the `Tests` class stays per-project; the per-project `<StartupObject>` in each csproj is unchanged). Both csproj files add the shared `<Compile Include>`.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests` → 63 pass (56 baseline + the 7 new seam tests from BP-4/6/7/8); `dotnet run --project tests/NeoVisual.Tests` → 42 pass (38 + the 4 `Run_ActionsRegistry_*`); `--list` and the substring filter still work. No new tests (dedup).
- **Fails-if:** either suite fails to build (namespace/`using` conflict); the runner no longer discovers `Run_*` methods; `--list`/filter broken; a test count changes.

---

## 3. Unit tests to add/update (RED proof)

All new seam tests follow the `OverlayKeyHandler`/`TextMotionNavigator` pattern: dependency-free
classes the UI delegates to. **RED proof** = the test references an API that does not exist yet
(compile error) or asserts behavior the current code does not have (assertion failure); GREEN
after the merge. Each test is added in the SAME commit as its merge (A4).

| Merge | Test(s) | Project | RED proof | GREEN after |
|-------|---------|---------|-----------|-------------|
| C3 (BP-4) | `Run_ActionsRegistry_ContainsAllBuiltins`, `Run_ActionsRegistry_CaseInsensitive`, `Run_ActionsRegistry_UnknownFallsThrough`, `Run_ActionsRegistry_TelescopeMapsToFinder` | `tests/NeoVisual.Tests` | `Actions` doesn't exist → compile error | `Actions.Resolve` returns the 10 built-ins, case-insensitive, unknown→null; `FinderNames` maps all 5 telescope names |
| L5 (BP-6) | `Run_TelescopeLog_Prefix`, `Run_TelescopeLog_EmptyMessage` | `tests/Telescope.Tests` | `TelescopeLog` doesn't exist → compile error | `TelescopeLog.Log("hello")` writes `[Telescope] hello` to the log file; `Log("")` writes `[Telescope] `; extends `Run_LogPrefixes_Pinned` |
| L7 (BP-7) | `Run_CaretPlacement_EnterInsertMapsToActions`, `Run_CaretPlacement_EnumValues` | `tests/Telescope.Tests` | `CaretPlacement` doesn't exist → compile error | `I→EnterInsert`, `A→EnterInsertAppend`; `EnterInsertMode(End/Start/Current)` map to the right `OverlayAction`; the 3 enum members exist |
| L6 (BP-8) | `Run_GetCandidates_DefaultQuery_MatchesNoArg`, `Run_GetCandidates_DefaultQuery_GrepEmpty`, `Run_GetCandidates_IsQueryDriven` | `tests/Telescope.Tests` | `GetCandidates(string)` signature change — `finder.GetCandidates("")` does not compile before the fold | `GetCandidates()` == `GetCandidates("")` (FileFinder); `GrepFinder.GetCandidates()` == `GetCandidates("")` == empty; `IsQueryDriven` true for Grep, false for FileFinder |

**Existing tests that must stay green (no change):** all 56 `tests/Telescope.Tests` (incl.
`Run_KeyHandler_IAEnterInsertWithCaret`, `Run_LogPrefixes_Pinned`, `Run_LogFileWriter_WritesAndClearsFile`,
`Run_GrepFinder_EmptyQueryReturnsZeroCandidates`) and all 38 `tests/NeoVisual.Tests` (incl.
`Run_Keybinding_DefaultFileHasTelescopeAndNav`, `Run_RectCoordinate_StoresFields`).

---

## 4. E2E gate (deferred — informational only)

No NEW e2e scenarios are needed — every merge is behavior-preserving, so the e2e gate is the
**existing scenarios staying green**. The affected existing scenarios per merge (from the plan's
§10 Lane 1 queue) are QUEUED in `e2e-queue.md` and NOT executed on this machine:

- **C1:** hook install + navigation + telescope-open
- **C6+L4:** telescope-references/implementation; telescope-issues/grep — **note:** references/implementation opens now emit an extra `[Telescope] goto line={line}` line (the literal already exists for issues/grep; the harness never asserts on `goto line=` — grep-verified 0 hits in `tools/`). The deferred e2e should re-verify this is benign.
- **C2:** all telescope-*
- **C3:** neovisual-leader (+ KeybindingConfig units)
- **C4:** telescope-references/implementation
- **L5:** e2e (log lines are the contract)
- **L7:** telescope-prompt-motions (+ units)
- **L6:** telescope-grep
- **X3:** unit suites

Each asserts: the listed scenarios stay GREEN + `git diff` shows no log-literal drift.
Diagnostics depended on: the existing `[Telescope]`/`[NeoVisual]`/`[Hook]` lines (unchanged).

---

## 5. Verification Trace (Lane 1)

| failing test / scenario | implicated steps | expected diagnostic / pass signal |
|---|---|---|
| `Run_ActionsRegistry_*` (RED: `Actions` doesn't exist) | BP-4 | `[NeoVisual] leader-binding executed: ...` unchanged; `Actions.Resolve` returns the 10 built-ins, case-insensitive, unknown→null |
| `Run_TelescopeLog_*` (RED: `TelescopeLog` doesn't exist) | BP-6 | `TelescopeLog.Log("x")` emits `[Telescope] x` (extends `Run_LogPrefixes_Pinned`) |
| `Run_CaretPlacement_*` (RED: `CaretPlacement` doesn't exist) | BP-7 | `[Telescope] key=... mode=... handled=...` unchanged; `EnterInsert(CaretPlacement)` maps to the right `OverlayAction` |
| `Run_GetCandidates_DefaultQuery_*` (RED: `GetCandidates(string)` signature change) | BP-8 | `[Telescope] grep hits=...` unchanged; `GetCandidates()` == `GetCandidates("")` |
| `tests/Telescope.Tests` (63) | BP-1..BP-9 | all existing `Run_*` pass |
| `tests/NeoVisual.Tests` (42) | BP-1..BP-9 | all existing `Run_*` pass |
| `pwsh tools/check-doc-refs.ps1` | BP-3, BP-8 | exit 0 (doc-ref lint) |
| A3 log-literal check (`git diff`) | BP-1..BP-9 | no `[Telescope]`/`[NeoVisual]`/`[Hook]` **structured-log** literal changed |

**Known-RED allowlist (do NOT report as regressions):** none — `docs/progress.md` reports no
known-RED e2e scenario remains (all 35 green as of 2026-09-27); e2e is deferred to
`e2e-queue.md` anyway.

**Documented deviation (adjudicated, not a regression):** BP-3 collapses the 5
`[NeoVisual]`-prefixed **Debug.WriteLine error-path** literals (`Failed to open Telescope ...`)
into one parameterized form `Failed to open Telescope {finderName}`. These are failure-path
`Debug.WriteLine` only (never `NeoVisualLog.Log`, never in the structured exp log, never asserted
by the harness — grep-verified: no `Failed to open Telescope` in `tools/`). No `[Telescope]`
literal changes anywhere; no structured-log literal changes.

---

## KEY DECISIONS (do not second-guess)

- **BP-3 deviation:** the 5 `[NeoVisual]Failed to open Telescope ...` Debug.WriteLine error-path
  literals collapse into one parameterized form — failure-path only, never asserted, not in the
  structured exp log.
- **BP-4 deviation:** the registry stores `Func<InputHandler, TelescopeLauncher, Action>` factory
  delegates (not plain `Action`) so it can be a static field and stay hermetically testable;
  `Navigate`/`ToggleSolutionExplorer` become `internal`.
- **BP-7 decision:** the overlay's "a" (append) maps to `InsertEnd` (end of text), NOT
  `InsertAfter` (caret+1) — preserve existing behavior, do not "fix" it.
- **BP-8 decision:** `bool IsQueryDriven { get; }` is added to `IFinder` as the required
  replacement for the deleted `is IQueryFinder` check — always re-gathering would be a
  behavior/perf regression.
- **BP-2:** `DteFileOpener` lives in the Telescope project and is visible to `MyExtension` via
  the existing `<InternalsVisibleTo Include="MyExtension" />` (`Telescope.csproj:32`) — do not
  add a new IVT entry.
- **BP-5:** `Mef<T>` uses `DefaultExportProvider.GetExportedValue<T>`; verify
  `VisualStudioWorkspace`/`IVsEditorAdaptersFactoryService` resolve identically to the current
  `componentModel.GetService<T>()` before merging.
- **Doc-ref updates are mandatory** in BP-3 (`OpenTelescope`) and BP-8 (`IQueryFinder`/
  `IQueryFinder.cs`) — `pwsh tools/check-doc-refs.ps1` is the acceptance gate for those steps.


---

# Lane 2 — Vim-motion / caret cluster (Build Plan)

> **Lane: refactor (unit-only, e2e deferred).** Executes merges **T1 → T2 → T4 → T5 → T3**
> (one BP step per merge, BP-1..BP-5) from the architecture-consolidation plan §2. Every
> merge is **behavior-preserving**: no `[Telescope]`/`[NeoVisual]`/`[Hook]` log literal
> changes (M-M7 NOT triggered), no new vim motions, no new behavior. The gate is the two
> offline unit suites staying green (`dotnet run --project tests/Telescope.Tests` → 63,
> `tests/NeoVisual.Tests` → 42 — after Lane 1) + the NEW seam tests (BP-1, BP-2, BP-4) being RED before
> and GREEN after + `pwsh tools/check-doc-refs.ps1` exiting 0 after the BP-1 doc-ref
> update. e2e is deferred to `e2e-queue.md` — **no e2e scenario name appears in any
> Verify-with**; the e2e gate column below is informational only.
>
> **Known-RED allowlist:** none — `docs/progress.md` reports no known-RED e2e scenario
> remains (all 35 green as of 2026-09-27); e2e is deferred anyway.
>
> **Hard requirements honored:** net472 (no `IReadOnlySet<T>` — `ActionKeys` stays
> `IReadOnlyCollection<Keys>`); `ThreadHelper.ThrowIfNotOnUIThread()` on any new VS-API
> method; `CardinalMovment` typo untouched; `ExcludeAssets="runtime"` untouched.
>
> **Corrections to the plan table / brief (verified against source 2026-09-28):**
> 1. **T1 test scope:** the plan's `Run_TextMotionEngine_MapMotion_*` lists
>    "h/l/w/b/e/0/$/gg/G + a/A/I", but the tool-window `TextMotion` enum has only **8
>    members** (`Left/Right/NextWord/PrevWord/EndWord/InsertAfter/InsertEnd/InsertStart`,
>    `TextInputToolWindowController.cs:19-29`) and `MapMotion` maps only **h/l/w/b/e/a/A/I**
>    (`:94-112`). `0/$/gg/G` are preview-pane motions in `TextMotionNavigator` (Telescope),
>    NOT tool-window motions. The MapMotion tests cover the 8 real mappings + shift handling.
> 2. **T4 mode-drift claim:** "SolutionExplorer only on enter" is inaccurate — it styles on
>    BOTH enter (`SolutionExplorerController.cs:42`) and exit (`:52`). The real drift:
>    TextInput uses a private `ApplyCaretStyle()` (WPF box + editor view, `:298-323`),
>    SolutionExplorer uses `TextMotionHelper.StyleFocusedTextBox` (WPF box only), and
>    `GeneralToolWindowController` styles nothing (`:41,:43`).
> 3. **T5 veto consumption sites:** the veto is composed at `InputHandler.cs:82-85` but
>    consumed at **four** sites, not two: `:64-68` (`HasToolWindowActionKeys`), `:276`
>    (`ShouldRouteToolWindowKey`), `:393` (`ShouldRouteToolWindowKey` in
>    `ExitToolWindowInputMode`), `:416` (`IsTyping`). The veto collapses into the two
>    routing methods; the `IsTyping` call keeps the veto property (its `editorFocused`
>    param is semantically the veto).
> 4. **T2 W/B/E action keys:** SolutionExplorer's search-box motion keys W/B/E must stay in
>    `ActionKeys` (InputHandler routes them to `TryMove` only because they are in
>    `ActionKeys`, `InputHandler.cs:312`). They become table entries delegating to
>    `TryMoveFocusedSurface` (which returns false when no box is focused), so
>    `ActionKeys => _actions.Keys` stays literally true without breaking search-box w/b/e.
> 5. **T1 SolutionExplorer gate:** `SolutionExplorerController.TryMove` must gate on
>    `FindFocusedTextBox() != null` before calling `TryMoveFocusedSurface` — otherwise the
>    merged helper's arrow fallback would swallow h/l in the tree and replace the
>    `solution-explorer collapse/expand` diagnostics with `toolwindow-move key=H -> arrow
>    vk=...` (a diagnostic-contract break).

---

## Merges table (Lane 2)

| id | Merge | Current state (file:line, verified) | Target design (API signatures) | Blast radius | Coverage (unit tests) | e2e gate (deferred, informational) |
|----|-------|-----------------------------------|-------------------------------|--------------|----------------------|-------------------------------------|
| T1 | **TextMotionEngine** | `TextInputToolWindowController.cs:114-221` (`TryMove` :114-172 + `ApplyMotionToBox` :179-221) re-implements the whole `TextMotionHelper` pipeline; `TextMotionHelper.cs:62` has an **inverted dependency** (`TextMotion? motion = TextInputToolWindowController.MapMotion(key, shift)`); `TextMotionHelper.TryMoveFocusedTextBox` (`:51-101`, WPF box only) is called ONLY by `SolutionExplorerController` (`SolutionExplorerController.cs:187`, proxy-confirmed); `TextMotion` enum at `TextInputToolWindowController.cs:19-29`; `MapMotion` at `:94-112`; private `ApplyCaretStyle` at `:298-323`; `GetFocus`/`GetAsyncKeyState` DllImports at `:336-340` | `TextMotionHelper` owns `internal enum TextMotion` (8 members, moved) + `public static TextMotion? MapMotion(Keys key, bool shift)` (moved) + `public static bool TryMoveFocusedSurface(Keys key, ref bool isInputMode)` (WPF box → editor view → WinForms → arrow fallback) + `public static void ApplyEditorViewCaret(IWpfTextView view, bool isInputMode)` + `public static void StyleFocusedSurface(bool isInputMode)` (WPF box + editor view). `TextInputToolWindowController.TryMove(Keys) => TextMotionHelper.TryMoveFocusedSurface(key, ref _isInputMode)`; `EnterInputMode`/`ExitInputMode` => `StyleFocusedSurface(_isInputMode)`. A new vim motion = 2 edits (MapMotion + TextMotionNavigator) | TextMotionHelper, TextInputToolWindowController, SolutionExplorerController | **NEW** `Run_TextMotionEngine_MapMotion_*` (RED: `TextMotionHelper.MapMotion` doesn't exist → compile error); **UPDATE** `Run_TextInput_MapMotions` + `Run_TextInput_MapInsertMotions` (`TextInputToolWindowController.MapMotion` → `TextMotionHelper.MapMotion`) | neovisual-textinput-motions + explorer-open-searchbox + NeoVisual units |
| T2 | **ActionTable** | `ActionKeys` list + `TryMove` switch are 2 edits per key: `SolutionExplorerController.cs:168-180` (ActionKeys) + `:196-242` (TryMove switch); `TextInputToolWindowController.cs:86-87` (ActionKeys) + `:114-172` (TryMove); `InputHandler.cs:310-313` gates on `controller.ActionKeys.Contains(key)` | `private readonly Dictionary<Keys, Func<bool>> _actions` (built in ctor); `public IReadOnlyCollection<Keys> ActionKeys => _actions.Keys`; `public bool TryMove(Keys key) => _actions.TryGetValue(key, out var f) && f()` (after the SolutionExplorer search-box gate). hjkl also in the table. Adding an action key = 1 edit (one `_actions[key] = ...` line) | InputHandler (ActionKeys/TryMove consumers — reads only, no change) | **NEW** `Run_ActionTable_*` (RED: hjkl not in `ActionKeys` today — SolutionExplorer = O/Enter/R/M/A/W/B/E/G, TextInput = W/B/E/A) | all neovisual-explorer-* + textinput e2e |
| T4 | **ControllerBase** | `_isInputMode` + Enter/ExitInputMode boilerplate triplicated: `GeneralToolWindowController.cs:26-43`, `TextInputToolWindowController.cs:59-83`, `SolutionExplorerController.cs:26-46`; mode logic drifts (TextInput styles via private `ApplyCaretStyle` on both enter+exit; SolutionExplorer styles via `StyleFocusedTextBox` on both + return-focus on exit; GeneralToolWindowController styles nothing) | `internal abstract class ToolWindowControllerBase : IToolWindowController` with `protected readonly ToolWindowType _type`, `protected bool _isInputMode`, `protected ToolWindowControllerBase(ToolWindowType type)`, `public ToolWindowType Type => _type`, `public bool IsInputMode => _isInputMode`, `public virtual void EnterInputMode()` / `public virtual void ExitInputMode()` (flip `_isInputMode` + call `protected virtual void OnModeChanged()`), `public abstract bool TryMove(Keys key)`, `public abstract IReadOnlyCollection<Keys> ActionKeys { get; }`. TextInput overrides `OnModeChanged` → `StyleFocusedSurface(_isInputMode)`; SolutionExplorer overrides `OnModeChanged` → `StyleFocusedSurface(_isInputMode)` + `ExitInputMode` → capture query, `base.ExitInputMode()`, return-focus. Public ctors unchanged | 3 controllers; WindowManager unchanged | none (behavior-preserving) — existing `Run_ToolWindowMode_HjklMoves`, `Run_SolutionExplorer_ActionKeys`, `Run_TextInput_StartsInInsertMode`, `Run_TextInput_ActionKeys`, `Run_GeneralController_NoActionKeys` stay green | existing e2e suite |
| T5 | **GuardOwnsVeto** | veto composed in `InputHandler.cs:82-85` (`EditorFocusedVeto`), passed as pre-baked bool at `:64-68` (`HasToolWindowActionKeys`), `:276` + `:393` (`ShouldRouteToolWindowKey`), `:416` (`IsTyping`); `FocusGuard` cannot reason about the rule; truth table incomplete (`FocusGuard.cs:22-24,30-31`) | `FocusGuard.ShouldRouteToolWindowKey(bool isToolWindow, bool editorFocused, bool isInputMode, bool isTextInputSurface)` = `isToolWindow && !(editorFocused && !isInputMode && !isTextInputSurface)`; `FocusGuard.HasToolWindowActionKeys(bool isToolWindow, bool isInputMode, int actionKeyCount, bool editorFocused, bool isTextInputSurface)` = `isToolWindow && !isInputMode && actionKeyCount > 0 && (!editorFocused || isTextInputSurface)` (equivalent to today's `!EditorFocusedVeto`); `InputHandler` passes raw `_vsVim.IsEditorFocused` / `CurrentController?.IsInputMode == true` / `GeneralToolWindowController.IsTextInputType(_windowManager.Type)`; `EditorFocusedVeto` retained for the `IsTyping` call at `:416` only | FocusGuard + InputHandler | **NEW** `Run_FocusGuard_TruthTable_*` (RED: new signatures don't exist → compile error); **UPDATE** `Run_FocusGuard_EditorFocusedBlocksRouting`, `Run_FocusGuard_TreeFocusedAllowsRouting`, `Run_FocusGuard_InputModeBlocksActionKeys`, `Run_FocusGuard_ZeroActionKeysBlocks`, `Run_FocusGuard_NonToolWindowBlocks` to the new signatures | FocusGuard truth-table units + neovisual-explorer-move-editor-focus e2e |
| T3 | **ResolvePipeline** | expand→BuildForest→resolve→Select duplicated: `SolutionExplorerController.cs:284-318` (`SelectFirstSourceFile`) vs `:93-112` (`ReturnFocusToTree`); copies diverge in null-handling (ReturnFocusToTree checks `Count > 0` and skips silently; SelectFirstSourceFile checks `Count == 0` → logs `select none` + returns, and logs `select none` for each null) | `private static (EnvDTE.UIHierarchyItem? Target, string? Path) ResolveTreeItem(EnvDTE80.DTE2 dte2, Func<List<HierarchyNode>, string?> pick)` — expand first project node, `BuildForest`, `pick(forest)`, `pathToItem.TryGetValue`; shared by both callers (callers keep their divergent null semantics) | SolutionExplorerController only | none (VS-coupled) | explorer-open-navigation + explorer-open-searchbox e2e |

---

## BP-1 — T1: TextMotionEngine (TextMotionHelper owns the motion pipeline)

- **Files:**
  - MODIFY `MyExtension/ToolWindows/TextMotionHelper.cs`
  - MODIFY `MyExtension/ToolWindows/TextInputToolWindowController.cs`
  - MODIFY `MyExtension/ToolWindows/SolutionExplorerController.cs`
  - MODIFY `tests/NeoVisual.Tests/Program.cs`
  - MODIFY docs (doc-ref, required — `TryMoveFocusedTextBox` becomes un-greppable): `docs/architecture-review.md:408` (backticked bare `TryMoveFocusedTextBox` — **lint-gated**) and `docs/progress.md:1212` (backticked `TextMotionHelper.TryMoveFocusedTextBox` — consistency) → reword to `TryMoveFocusedSurface` so `pwsh tools/check-doc-refs.ps1` exits 0.
- **Change:**
  - `TextMotionHelper.cs`: add `using System;`, `using Microsoft.VisualStudio.Text;`, `using Microsoft.VisualStudio.Text.Editor;`. Move the `internal enum TextMotion` (8 members) and `public static TextMotion? MapMotion(Keys key, bool shift)` here verbatim from `TextInputToolWindowController.cs:19-29,94-112`. Rename `TryMoveFocusedTextBox` → `public static bool TryMoveFocusedSurface(Keys key, ref bool isInputMode)` and extend it to the full pipeline (the union of the current helper's WPF branch + `TextInputToolWindowController.TryMove`): WPF box (`FindFocusedTextBox`) → editor view (`Keyboard.FocusedElement is IWpfTextView`, `view.Caret.MoveTo` clamped) → WinForms (`FindFocusedWinFormsTextBox`) → arrow fallback for `TextMotion.Left/Right` (log `toolwindow-move key={key} -> arrow vk={vk} focused={focused} hwnd=0x{hwnd.ToInt64():X}` + `KeyInjection.Press(vk)`). Move the private `ApplyMotionToBox(string, int, Action<int>, Keys, TextMotion, bool styleCaret, ref bool isInputMode)` + `MotionName` + `FindFocusedWinFormsTextBox` + `GetParent` here; the insert side effect (`isInputMode = true` for `InsertAfter/InsertEnd/InsertStart`) and the two log formats (`textinput-enter-input {MotionName} caret={newCaret}` / `text-motion key={key} caret={newCaret} len={text.Length} text='{sample}'`) are preserved byte-identically. Add `public static void ApplyEditorViewCaret(IWpfTextView view, bool isInputMode)` = `BlockCaretAdornment.Attach(view).Active = !isInputMode` (try/catch, emits `block-caret active=True|False`). Rename `StyleFocusedTextBox(bool)` → `public static void StyleFocusedSurface(bool isInputMode)` = WPF box `ApplyCaretStyle(box, isInputMode)` + editor view `ApplyEditorViewCaret(view, isInputMode)`. **P/Invoke (M3 — do NOT add new DllImports):** the arrow-fallback log needs `GetFocus()` and the shift check needs `GetAsyncKeyState` — call `NativeMethods.GetFocus()` and `NativeMethods.GetAsyncKeyState(0x10)` (both moved to `NativeMethods` in Lane 1 BP-1). The `GetAsyncKeyState` decl at `TextMotionHelper.cs:169-170` was deleted in Lane 1 BP-1 — do not re-add it.
  - `TextInputToolWindowController.cs`: delete `TextMotion` enum (`:19-29`), `MapMotion` (`:94-112`), `ApplyMotionToBox` (`:179-221`), `MotionName` (`:223-232`), `FindFocusedTextBox` (`:239-251`), `FindFocusedWinFormsTextBox` (`:256-267`), `GetParent` (`:269-291`), `ApplyCaretStyle` (`:298-323`), `BlockCaretBrush`/`CreateBlockBrush` (`:325-334`). **Note (M3):** the `GetFocus`/`GetAsyncKeyState` DllImports (`:336-340`) were ALREADY removed in Lane 1 BP-1 (moved to `NativeMethods`) — only the call sites remain to delete/rewrite to `NativeMethods.X`. `TryMove(Keys key)` (`:114-172`) → `return TextMotionHelper.TryMoveFocusedSurface(key, ref _isInputMode);`. `EnterInputMode`/`ExitInputMode` (`:73-83`) → `_isInputMode = true/false; TextMotionHelper.StyleFocusedSurface(_isInputMode);`. Keep `_type`, ctor, `Type`, `IsInputMode`, `ActionKeys` (`:86-87`).
  - `SolutionExplorerController.cs`: `TryMove` (`:182-243`) — gate the search-box branch on `FindFocusedTextBox() != null` BEFORE calling the helper (prevents the merged arrow fallback from swallowing h/l in the tree): `if (TextMotionHelper.FindFocusedTextBox() != null) { return TextMotionHelper.TryMoveFocusedSurface(key, ref _isInputMode); }` then the existing switch unchanged (T2 replaces it). `EnterInputMode`/`ExitInputMode` (`:39-64`) delegate styling to `TextMotionHelper.StyleFocusedSurface(_isInputMode)` (T4 formalizes this).
  - `tests/NeoVisual.Tests/Program.cs`: update `Run_TextInput_MapMotions` (`:331-338`) + `Run_TextInput_MapInsertMotions` (`:340-349`) to call `TextMotionHelper.MapMotion(...)`; add the NEW `Run_TextMotionEngine_MapMotion_*` tests (below).
- **Verify-with:** NEW tests in `tests/NeoVisual.Tests` (RED: `TextMotionHelper.MapMotion` doesn't exist → compile error): `Run_TextMotionEngine_MapMotion_LeftRight` (`MapMotion(Keys.H,false)==TextMotion.Left`, `MapMotion(Keys.L,false)==TextMotion.Right`), `Run_TextMotionEngine_MapMotion_Words` (`W→NextWord`, `B→PrevWord`, `E→EndWord`), `Run_TextMotionEngine_MapMotion_InsertShift` (`A,true→InsertEnd`, `A,false→InsertAfter`, `I,true→InsertStart`, `I,false→null`), `Run_TextMotionEngine_MapMotion_UnknownNull` (`X→null`). Updated `Run_TextInput_MapMotions`/`Run_TextInput_MapInsertMotions` pass via `TextMotionHelper.MapMotion`. Diagnostics preserved byte-identically: `[NeoVisual] text-motion key=... caret=... len=... text='...'`, `[NeoVisual] textinput-enter-input after|end|start caret=...`, `[NeoVisual] toolwindow-move key=... -> arrow vk=... focused=... hwnd=0x...`, `[NeoVisual] block-caret active=True|False`, `[NeoVisual] solution-explorer collapse/expand`. `pwsh tools/check-doc-refs.ps1` exits 0 (after the doc-ref update).
- **Fails-if:** `Run_TextMotionEngine_MapMotion_*` fails; `Run_TextInput_MapMotions`/`Run_TextInput_MapInsertMotions` no longer compile (MapMotion not found); `[NeoVisual] text-motion key=...` / `textinput-enter-input ...` / `toolwindow-move key=... -> arrow vk=...` / `block-caret active=...` no longer emitted or change format; `solution-explorer collapse/expand` replaced by `toolwindow-move key=H -> arrow vk=...` (the gate was missed); doc-ref lint fails on `TryMoveFocusedTextBox`.

## BP-2 — T2: ActionTable (one edit per action key)

- **Files:**
  - MODIFY `MyExtension/ToolWindows/SolutionExplorerController.cs`
  - MODIFY `MyExtension/ToolWindows/TextInputToolWindowController.cs`
  - MODIFY `tests/NeoVisual.Tests/Program.cs`
- **Change:** both controllers replace the `ActionKeys` list + `TryMove` switch with a `private readonly Dictionary<Keys, Func<bool>> _actions` built in the ctor; `public IReadOnlyCollection<Keys> ActionKeys => _actions.Keys` (net472 — stays `IReadOnlyCollection<Keys>`, no `IReadOnlySet<T>`); `public bool TryMove(Keys key) => _actions.TryGetValue(key, out var f) && f();`.
  - `SolutionExplorerController`: `TryMove` = search-box gate (`if (TextMotionHelper.FindFocusedTextBox() != null) return TextMotionHelper.TryMoveFocusedSurface(key, ref _isInputMode);`) then `_actions.TryGetValue(...)`. `_actions` entries (each `() => { ...; return true; }`): `[Keys.I]`→`FocusSearchBox()`, `[Keys.O]`/`[Keys.Enter]`→`OpenSelected()`, `[Keys.R]`→`RenameSelected()`, `[Keys.M]`→`MoveSelected()`, `[Keys.A]`→`AddItem()`, `[Keys.G]`→`SelectFirstSourceFile()`, `[Keys.H]`→log `solution-explorer collapse` + `KeyInjection.Press(VK_LEFT)`, `[Keys.L]`→log `solution-explorer expand` + `KeyInjection.Press(VK_RIGHT)`, `[Keys.J]`→log `toolwindow-move key=J -> arrow vk=40` + `KeyInjection.Press(VK_DOWN)`, `[Keys.K]`→log `toolwindow-move key=K -> arrow vk=38` + `KeyInjection.Press(VK_UP)`, and `[Keys.W]`/`[Keys.B]`/`[Keys.E]`→`() => TextMotionHelper.TryMoveFocusedSurface(Keys.W/B/E, ref _isInputMode)` (keeps the search-box motion keys in `ActionKeys`; returns false when no box is focused, preserving the current fall-through). Delete the `switch` (`:196-242`) and the `KeyToArrowVk` helper (`:482-490`).
  - `TextInputToolWindowController`: `TryMove` = `_actions.TryGetValue(...)`. `_actions` entries: `[Keys.W]`→`() => TextMotionHelper.TryMoveFocusedSurface(Keys.W, ref _isInputMode)`, `[Keys.B]`→`... (Keys.B, ...)`, `[Keys.E]`→`... (Keys.E, ...)`, `[Keys.A]`→`... (Keys.A, ...)`, `[Keys.H]`→`... (Keys.H, ...)`, `[Keys.L]`→`... (Keys.L, ...)`. (H/L are the Left/Right motions; J/K are not motions and stay out of the table — they return false, as today.) `ActionKeys` becomes `{W,B,E,A,H,L}`.
  - `InputHandler.cs:310-313` is a **consumer, not modified** — `controller.ActionKeys.Contains(key)` now reads `_actions.Keys`.
- **Verify-with:** NEW tests in `tests/NeoVisual.Tests` (RED: hjkl are not in `ActionKeys` today — SolutionExplorer = O/Enter/R/M/A/W/B/E/G, TextInput = W/B/E/A — so the hjkl-in-ActionKeys assertions fail): `Run_ActionTable_SolutionExplorer_HjklInActionKeys` (`new SolutionExplorerController(() => null!)` → `ActionKeys` contains H/J/K/L), `Run_ActionTable_TextInput_HjklInActionKeys` (`new TextInputToolWindowController(ToolWindowType.CommandWindow)` → `ActionKeys` contains H/L), `Run_ActionTable_SolutionExplorer_ActionKeysMatchTable` (`ActionKeys` == {O,Enter,R,M,A,G,W,B,E,H,J,K,L,I}), `Run_ActionTable_TextInput_ActionKeysMatchTable` (`ActionKeys` == {W,B,E,A,H,L}), `Run_ActionTable_UnmappedKeyNotConsumed` (`TryMove(Keys.X)` false for both), `Run_ActionTable_SolutionExplorer_ConsumesMappedKeys` (`TryMove(O/R/M/A/G)` true — extends the existing `Run_SolutionExplorer_ActionKeys`). Existing `Run_SolutionExplorer_ActionKeys`, `Run_TextInput_ActionKeys`, `Run_TextInput_StartsInInsertMode` stay green. Diagnostics unchanged: `[NeoVisual] solution-explorer open/rename/move/add/select file=.../collapse/expand`, `[NeoVisual] toolwindow-move key=... -> arrow vk=...`, `[NeoVisual] text-motion key=... caret=...`.
- **Fails-if:** `Run_ActionTable_*` fails; a search-box w/b/e motion stops working (W/B/E dropped from `ActionKeys`); `TryMove` returns false for a mapped key; `[NeoVisual] solution-explorer open` no longer fires for `o`/Enter.

## BP-3 — T4: ControllerBase (one mode-state owner)

- **Files:**
  - CREATE `MyExtension/ToolWindows/ToolWindowControllerBase.cs`
  - MODIFY `MyExtension/ToolWindows/GeneralToolWindowController.cs`
  - MODIFY `MyExtension/ToolWindows/TextInputToolWindowController.cs`
  - MODIFY `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** `internal abstract class ToolWindowControllerBase : IToolWindowController` (namespace `MyExtension`, `using System.Windows.Forms;` + `using System.Collections.Generic;`): `protected readonly ToolWindowType _type; protected bool _isInputMode; protected ToolWindowControllerBase(ToolWindowType type) { _type = type; }`, `public ToolWindowType Type => _type;`, `public bool IsInputMode => _isInputMode;`, `public virtual void EnterInputMode() { _isInputMode = true; OnModeChanged(); }`, `public virtual void ExitInputMode() { _isInputMode = false; OnModeChanged(); }`, `protected virtual void OnModeChanged() { }`, `public abstract bool TryMove(Keys key);`, `public abstract IReadOnlyCollection<Keys> ActionKeys { get; }`. The three controllers inherit:
  - `GeneralToolWindowController(ToolWindowType type) : base(type)` — ctor body sets `_isInputMode = IsTextInputType(type)`; inherits the base Enter/Exit (no styling — behavior preserved); keeps `IsTextInputType` static + `TryMove`/`ActionKeys` as-is.
  - `TextInputToolWindowController(ToolWindowType type) : base(type)` — ctor body sets `_isInputMode = true`; `protected override void OnModeChanged() => TextMotionHelper.StyleFocusedSurface(_isInputMode);` (styles on both enter+exit, preserving today's `ApplyCaretStyle()` calls at `:76,:82`); `TryMove`/`ActionKeys` from BP-1/BP-2.
  - `SolutionExplorerController(Func<EnvDTE.DTE> dteFactory) : base(ToolWindowType.SolutionExplorer)` — ctor body sets `_isInputMode = false`; `protected override void OnModeChanged() => TextMotionHelper.StyleFocusedSurface(_isInputMode);` (styles on both, preserving `:42,:52`); `public override void ExitInputMode() { ThreadHelper.ThrowIfNotOnUIThread(); string query = TextMotionHelper.FindFocusedTextBox()?.Text ?? string.Empty; base.ExitInputMode(); if (TextMotionHelper.FindFocusedTextBox() != null) ReturnFocusToTree(query); else ExecuteCommand("View.SolutionExplorer"); }` (query captured BEFORE the mode flip, preserving the current order); `TryMove`/`ActionKeys` from BP-1/BP-2.
  - Public ctors unchanged (`GeneralToolWindowController(ToolWindowType)`, `TextInputToolWindowController(ToolWindowType)`, `SolutionExplorerController(Func<EnvDTE.DTE>)`) — `WindowManager.GetController` (`WindowManager.cs:74-99`) and the existing tests construct them unchanged.
- **Verify-with:** `dotnet build`; both unit suites stay green — `tests/NeoVisual.Tests` (42, after Lane 1) incl. `Run_ToolWindowMode_HjklMoves`, `Run_SolutionExplorer_ActionKeys`, `Run_TextInput_StartsInInsertMode`, `Run_TextInput_ActionKeys`, `Run_GeneralController_NoActionKeys`; `tests/Telescope.Tests` (63). No new unit tests (behavior-preserving). Diagnostics unchanged: `[NeoVisual] toolwindow-enter-input` / `toolwindow-exit-input`, `[NeoVisual] textinput-enter-input ...`, `[NeoVisual] block-caret active=True|False`, `[NeoVisual] solution-explorer search-focus`.
- **Fails-if:** a controller ctor signature changes (existing tests fail to compile); `TextInputToolWindowController` no longer styles the caret on enter/exit; `SolutionExplorerController.ExitInputMode` reads the query AFTER the mode flip (return-focus breaks); `toolwindow-enter-input`/`toolwindow-exit-input` no longer emitted.

## BP-4 — T5: GuardOwnsVeto (FocusGuard owns the veto rule)

- **Files:**
  - MODIFY `MyExtension/ToolWindows/FocusGuard.cs`
  - MODIFY `MyExtension/InputHandler.cs`
  - MODIFY `tests/NeoVisual.Tests/Program.cs`
- **Change:**
  - `FocusGuard.cs`: `public static bool ShouldRouteToolWindowKey(bool isToolWindow, bool editorFocused, bool isInputMode, bool isTextInputSurface) => isToolWindow && !(editorFocused && !isInputMode && !isTextInputSurface);` and `public static bool HasToolWindowActionKeys(bool isToolWindow, bool isInputMode, int actionKeyCount, bool editorFocused, bool isTextInputSurface) => isToolWindow && !isInputMode && actionKeyCount > 0 && (!editorFocused || isTextInputSurface);` (both provably equivalent to today's `!EditorFocusedVeto` composition — the "text-input surface owns the keyboard" exception is now visible to the guard). `IsTyping` unchanged.
  - `InputHandler.cs`: the three routing call sites pass raw inputs instead of the pre-baked veto — `:64-68` → `FocusGuard.HasToolWindowActionKeys(_windowManager.IsToolWindow, c?.IsInputMode == true, c?.ActionKeys.Count ?? 0, _vsVim.IsEditorFocused, GeneralToolWindowController.IsTextInputType(_windowManager.Type))`; `:276` and `:393` → `FocusGuard.ShouldRouteToolWindowKey(_windowManager.IsToolWindow, _vsVim.IsEditorFocused, _windowManager.CurrentController?.IsInputMode == true, GeneralToolWindowController.IsTextInputType(_windowManager.Type))`. The `EditorFocusedVeto` property (`:82-85`) is **retained** for the `IsTyping` call at `:416` only (its `editorFocused` param is semantically the veto) — documented in KEY DECISIONS.
  - `tests/NeoVisual.Tests/Program.cs`: update the 5 existing FocusGuard tests to the new signatures (same assertions): `Run_FocusGuard_EditorFocusedBlocksRouting` (`ShouldRouteToolWindowKey(true,true,false,false)` false; `HasToolWindowActionKeys(true,false,5,true,false)` false), `Run_FocusGuard_TreeFocusedAllowsRouting` (`(true,false,false,false)` true; `(true,false,5,false,false)` true), `Run_FocusGuard_InputModeBlocksActionKeys` (`(true,true,5,false,false)` false), `Run_FocusGuard_ZeroActionKeysBlocks` (`(true,false,0,false,false)` false), `Run_FocusGuard_NonToolWindowBlocks` (`(false,false,5,false,false)` false; `(false,false,false,false)` false). Add the NEW truth-table tests (below).
- **Verify-with:** NEW tests in `tests/NeoVisual.Tests` (RED: the new signatures don't exist → compile error): `Run_FocusGuard_TruthTable_TextInputSurfaceOwnsKeyboard` (`ShouldRouteToolWindowKey(true, editorFocused:true, isInputMode:false, isTextInputSurface:true)` true — the exception), `Run_FocusGuard_TruthTable_InputModeOwnsKeyboard` (`(true,true,true,false)` true), `Run_FocusGuard_TruthTable_EditorVetoesNavigation` (`(true,true,false,false)` false), `Run_FocusGuard_TruthTable_NonToolWindowNeverRoutes` (`(false,false,false,false)` false), `Run_FocusGuard_TruthTable_ActionKeysTextInputSurface` (`HasToolWindowActionKeys(true,false,5,true,true)` true), `Run_FocusGuard_TruthTable_ActionKeysEditorVeto` (`HasToolWindowActionKeys(true,false,5,true,false)` false). Updated existing FocusGuard tests pass. Diagnostics unchanged: `[NeoVisual] toolwindow-move key=... -> arrow vk=...`, `[NeoVisual] toolwindow-enter-input` / `toolwindow-exit-input`.
- **Fails-if:** `Run_FocusGuard_TruthTable_*` fails; an existing FocusGuard test no longer compiles (signature not updated); a text-input surface (Command Window) with a stale editor flag stops routing keys (the exception was lost); an editor-focused `m` fires a tree action (the leak-guard regression the veto exists to prevent).

## BP-5 — T3: ResolvePipeline (one expand→resolve→Select)

- **Files:**
  - MODIFY `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** add `private static (EnvDTE.UIHierarchyItem? Target, string? Path) ResolveTreeItem(EnvDTE80.DTE2 dte2, Func<System.Collections.Generic.List<HierarchyNode>, string?> pick)` — `var seh = dte2.ToolWindows.SolutionExplorer; if (seh.UIHierarchyItems.Count == 0) return (null, null); var solutionNode = seh.UIHierarchyItems.Item(1); var projectNode = FindFirstProjectNode(solutionNode); if (projectNode == null) return (null, null); projectNode.UIHierarchyItems.Expanded = true; var forest = new List<HierarchyNode>(); var pathToItem = new Dictionary<string, EnvDTE.UIHierarchyItem>(StringComparer.OrdinalIgnoreCase); BuildForest(projectNode, forest, pathToItem); string? path = pick(forest); if (path == null) return (null, null); return (pathToItem.TryGetValue(path, out var t) ? t : null, path);`. Refactor `ReturnFocusToTree` (`:93-112`) → `var (target, _) = ResolveTreeItem(dte2, forest => HierarchyResolver.FirstPathMatching(forest, query));` then the existing Escape/Select/keeper logic unchanged. Refactor `SelectFirstSourceFile` (`:284-318`) → keep the `dte`/`dte2` null checks, then `var (item, first) = ResolveTreeItem(dte2, HierarchyResolver.FirstSourceFilePath); if (first == null) { log `solution-explorer select none`; return; } item!.Select(...)` then the existing open/keeper/log logic unchanged (`item` is non-null whenever `first != null` — the path came from the forest that populated `pathToItem`). The divergent null-handling stays at the CALLER level (ReturnFocusToTree skips silently; SelectFirstSourceFile logs `select none`).
- **Verify-with:** `dotnet build`; both unit suites stay green (`tests/NeoVisual.Tests` 38 incl. `Run_SolutionExplorer_ActionKeys` which constructs the controller with `() => null!`; `tests/Telescope.Tests` 56). No new unit tests (VS-coupled). Diagnostics unchanged: `[NeoVisual] solution-explorer select file=...` / `select none`, `[NeoVisual] editor-view-opened file=...`, `[NeoVisual] solution-explorer search-focus`.
- **Fails-if:** `ResolveTreeItem` returns a null `Target` when `Path` is non-null (SelectFirstSourceFile's `item!` NREs); `select file=...` no longer emitted for the first source file; `select none` emitted twice for one failure; `ReturnFocusToTree` no longer selects the query-matched item.

---

## Unit test plan (RED proof)

All new seam tests follow the `OverlayKeyHandler`/`TextMotionNavigator` pattern (dependency-free classes the UI delegates to). **RED proof** = the test references an API that does not exist yet (compile error) or asserts behavior the current code does not have (assertion failure); GREEN after the merge.

- **tests/NeoVisual.Tests** (add ~16, update ~7):
  - `Run_TextMotionEngine_MapMotion_*` (BP-1, T1) — `TextMotionHelper.MapMotion` key→`TextMotion` mapping: h/l/w/b/e/a/A/I + shift handling (8 members; **not** 0/$/gg/G — those are preview-pane `TextMotionNavigator` motions, see Corrections). **RED:** `TextMotionHelper.MapMotion` doesn't exist → compile error. **UPDATE:** `Run_TextInput_MapMotions` + `Run_TextInput_MapInsertMotions` → `TextMotionHelper.MapMotion`.
  - `Run_ActionTable_*` (BP-2, T2) — `ActionKeys => _actions.Keys`, `TryMove` → `TryGetValue`, hjkl in the table. **RED:** hjkl not in `ActionKeys` today (SolutionExplorer = O/Enter/R/M/A/W/B/E/G; TextInput = W/B/E/A) → assertion failure.
  - `Run_FocusGuard_TruthTable_*` (BP-4, T5) — `ShouldRouteToolWindowKey(isToolWindow, editorFocused, isInputMode, isTextInputSurface)` + `HasToolWindowActionKeys(..., isTextInputSurface)` full truth table incl. the "text-input surface owns the keyboard" exception. **RED:** new signatures don't exist → compile error. **UPDATE:** the 5 existing FocusGuard tests to the new signatures.
- **tests/Telescope.Tests** — no changes (Lane 2 touches no Telescope code; `TextMotionNavigator` is untouched).

## Verification Trace (Lane 2)

| failing test / scenario | implicated steps | expected diagnostic / pass signal |
|---|---|---|
| `Run_TextMotionEngine_MapMotion_*` (RED: `TextMotionHelper.MapMotion` doesn't exist) | BP-1 | `TextMotionHelper.MapMotion` returns the 8 mappings; `[NeoVisual] text-motion key=... caret=...` / `textinput-enter-input ...` unchanged |
| `Run_TextInput_MapMotions` / `Run_TextInput_MapInsertMotions` (updated to `TextMotionHelper.MapMotion`) | BP-1 | compile + pass after the move |
| `Run_ActionTable_*` (RED: hjkl not in `ActionKeys`) | BP-2 | `ActionKeys == _actions.Keys` incl. hjkl; `TryMove` consumes mapped keys; `[NeoVisual] solution-explorer open/...` unchanged |
| `Run_FocusGuard_TruthTable_*` (RED: new signatures don't exist) | BP-4 | full truth table incl. the text-input-surface exception; `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged |
| 5 existing FocusGuard tests (updated signatures) | BP-4 | same assertions pass with the new 4-arg/5-arg calls |
| `tests/NeoVisual.Tests` (42) | BP-1..BP-5 | all existing `Run_*` pass |
| `tests/Telescope.Tests` (63) | BP-1..BP-5 | all existing `Run_*` pass (no Telescope code touched) |
| `pwsh tools/check-doc-refs.ps1` | BP-1 | exit 0 (doc-ref lint; `TryMoveFocusedTextBox` reworded) |
| A3 log-literal check (`git diff`) | BP-1..BP-5 | no `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal changed |

**Known-RED allowlist (do NOT report as regressions):** none — `docs/progress.md` reports no known-RED e2e scenario remains (all 35 green as of 2026-09-27); e2e is deferred to `e2e-queue.md` anyway.

## KEY DECISIONS (Lane 2 — do not second-guess)

- **T1 SolutionExplorer gate is mandatory.** `SolutionExplorerController.TryMove` gates on `FindFocusedTextBox() != null` before `TryMoveFocusedSurface`; without it the merged arrow fallback swallows h/l in the tree and replaces `solution-explorer collapse/expand` with `toolwindow-move key=H -> arrow vk=...` (diagnostic-contract break).
- **T2 W/B/E stay in `ActionKeys`** as table entries delegating to `TryMoveFocusedSurface` (returns false when no box is focused). They are the search-box motion keys; dropping them breaks search-box w/b/e. `ActionKeys => _actions.Keys` stays literally true.
- **T5 `EditorFocusedVeto` is retained for the `IsTyping` call only** (`InputHandler.cs:416`); the veto collapses into `ShouldRouteToolWindowKey` + `HasToolWindowActionKeys` (both take raw inputs). `HasToolWindowActionKeys` gains `isTextInputSurface` even though the plan table names only `ShouldRouteToolWindowKey` — it receives the same pre-baked veto at `:68`, so it must take the raw inputs too or the veto stays half-baked.
- **T4 public ctors are frozen** (`GeneralToolWindowController(ToolWindowType)`, `TextInputToolWindowController(ToolWindowType)`, `SolutionExplorerController(Func<EnvDTE.DTE>)`) — `WindowManager.GetController` and the existing tests construct them unchanged.
- **T1 MapMotion tests cover only the 8 real tool-window motions** (h/l/w/b/e/a/A/I). `0/$/gg/G` are preview-pane `TextMotionNavigator` motions and are NOT added to the tool-window enum (that would be new behavior, forbidden in this refactor lane).
- **Doc-ref:** only `TryMoveFocusedTextBox` is renamed; the lint-gated reference is `docs/architecture-review.md:408` (bare backticked token); `docs/progress.md:1212` is updated for consistency (dotted token is not lint-checked).


---

# Lane 3 — Finder cluster (Build Plan)

> **Lane: refactor (unit-only, e2e deferred).** Executes the Lane 3 merges of the
> architecture-consolidation program (`docs/architecture-consolidation.md`, 2026-09-28)
> in the lane order **L3 → L2 → L1 → C5 → C7** (BP-1..BP-5, one BP step per merge).
> Every merge is behavior-preserving; the finder cluster is the prerequisite for the
> deferred fzf finder roadmap.
>
> **Gate:** both offline unit suites stay green (`dotnet run --project tests/Telescope.Tests`
> → 63, `tests/NeoVisual.Tests` → 58 — after Lanes 1-2); the new seam tests (BP-1, BP-2) are RED before the
> merge and GREEN after; `git diff` shows no change to any `[Telescope]`/`[NeoVisual]`/`[Hook]`
> log literal (M-M7 NOT triggered); e2e is deferred to `e2e-queue.md` — **no e2e scenario name
> appears in any Verify-with** (the e2e gate column below is informational only).
>
> **Known-RED allowlist:** none — `docs/progress.md` reports no known-RED e2e scenario
> remains (all 35 green as of 2026-09-27); e2e is deferred anyway.
>
> **Prerequisite (already executed by the time this lane runs):** Lane 1's L6 (BP-8) folded
> the query into `IFinder` — `GetCandidates(string query = "")` + `bool IsQueryDriven { get; }`,
> and deleted `IQueryFinder`. `FinderBase<THit>` implements that post-L6 `IFinder` signature.

## Lane 3 merges table (verified against source)

| id | Current state (file:line, verified) | Target design (exact API signatures) | Blast radius | Coverage (unit tests) | e2e gate (deferred, informational) |
|----|-------------------------------------|--------------------------------------|--------------|----------------------|-----------------------------------|
| L3 | 4 hit models hand-roll `FilePath`+`LineNumber` with identical null-coalescing ctors: `CodeIssue.cs:24-38` (ctor :26-32, props :34-37), `GrepHit.cs:8-20` (ctor :10-15, props :17-19), `ReferenceHit.cs:10-28` (ctor :12-20, props :22-27), `ImplementationHit.cs:10-24` (ctor :12-18, props :20-23) | `public interface IFileLocation { string FilePath { get; } int LineNumber { get; } }`; `public abstract class FileLocation : IFileLocation { protected FileLocation(string filePath, int lineNumber); public string FilePath { get; } public int LineNumber { get; } }` (null-coalescing ctor); `public sealed class FileHit : FileLocation { public FileHit(string filePath, int lineNumber); }`; the 4 hit models become `: FileLocation` with **unchanged** public ctors/properties (CodeIssue adds Kind/Text, GrepHit adds LineText, ReferenceHit adds Column/IsWrite/Symbol/LineText, ImplementationHit adds SymbolName/Kind) | finder ctors + host gatherers (`MyExtensionPackage` constructs `ReferenceHit`/`ImplementationHit`) + overlay preview branches + finder tests | **NEW** `Run_FileLocation_*` + `Run_IFileLocation_*` (RED: classes don't exist); existing hit-model tests stay green | telescope-issues/grep/references/implementation/preview (deferred) |
| L2 | 5 finder skeletons re-implement Name/UI-thread assert/try-catch gather/test-seam/ToEntry/OnSelected: `FileFinder.cs:18-191`, `CodeIssuesFinder.cs:25-231`, `GrepFinder.cs:20-182`, `ReferencesFinder.cs:24-99`, `ImplementationFinder.cs:25-97` | `public abstract class FinderBase<THit> : IFinder where THit : IFileLocation` with `public abstract string Name { get; }`, `protected abstract IReadOnlyList<THit> GatherHits();`, `protected abstract FinderEntry ToEntry(THit hit);`, `protected abstract void OpenHit(THit hit);`, `public virtual IReadOnlyList<FinderEntry> GetCandidates(string query = "")`, `public void OnSelected(FinderEntry entry)`, `protected void AssertUiThread()`, error-literal hooks. Base owns GetCandidates/OnSelected/AssertUiThread/error-handling; subclasses keep current public ctors (package registration unchanged). Adding a finder ≈ 20 lines | MyExtensionPackage registration (public ctors unchanged) + finder tests | **NEW** `Run_FinderBase_*` (RED: class doesn't exist); existing finder tests stay green (with `Run_FileFinder_*` payload updates) | telescope-open-file/issues/grep/references/implementation/preview (deferred) |
| L1 | 5 near-identical preview branches `TelescopeOverlay.cs:442-539` (CodeIssue :442-461, ReferenceHit :463-482, ImplementationHit :484-503, GrepHit :505-524, string path :526-539): File.Exists → ReadAllText → SetPreviewContent → MoveToLine → ApplyPreviewCaret → 2 logs | `internal static class PreviewRenderer { public static void Show(RichTextBox previewBox, TextMotionNavigator navigator, IFileLocation location); public static void SetContent(RichTextBox previewBox, TextMotionNavigator navigator, string content); public static void ApplyCaret(RichTextBox previewBox, TextMotionNavigator navigator); }` — owns read/tokenize/caret/log; overlay collapses to ONE `payload is IFileLocation` branch | TelescopeOverlay only | `Run_IFileLocation_*` (added in BP-1, GREEN by BP-3); existing `Run_TextMotionNavigator_*` MoveToLine tests stay green | telescope-preview/issues/grep/references/implementation (deferred) |
| C5 | `GatherReferences`/`GatherImplementations` share an identical ~50-line prologue (`MyExtensionPackage.cs:488-536` and `:570-612`); `GetSyntaxRootAsync` result unused at `:527-528` and `:607-608` | `private bool TryGetCaretSymbol(out VisualStudioWorkspace workspace, out Document document, out ISymbol symbol)` — both gatherers call it then diverge at `FindReferencesAsync`/`FindImplementationsAsync`; the unused `GetSyntaxRootAsync` call is dropped | MyExtensionPackage only | none (VS-coupled) | telescope-references/implementation (deferred) |
| C7 | split dispatch: explicit `RegisterController` for SolutionExplorer (`MyExtensionPackage.cs:85`), implicit lazy branches for text-input/general (`WindowManager.cs:68-72` RegisterController, `:89-98` GetController lazy branches) | register ALL controllers explicitly in `InitializeAsync` (SolutionExplorer + a `TextInputToolWindowController` per text-input type + a `GeneralToolWindowController` per other type); `GetController` = pure `_controllers.TryGetValue(type, out var c) ? c : _defaultController` | WindowManager + package init | none (VS-coupled) | neovisual-toolwindow/explorer-* (deferred) |

---

## BP-1 — L3: FileLocation (+ IFileLocation + FileHit)

- **Files:**
  - CREATE `Telescope/IFileLocation.cs`
  - CREATE `Telescope/FileLocation.cs`
  - CREATE `Telescope/FileHit.cs`
  - MODIFY `Telescope/CodeIssue.cs` (`CodeIssue : FileLocation`)
  - MODIFY `Telescope/GrepHit.cs` (`GrepHit : FileLocation`)
  - MODIFY `Telescope/ReferenceHit.cs` (`ReferenceHit : FileLocation`)
  - MODIFY `Telescope/ImplementationHit.cs` (`ImplementationHit : FileLocation`)
  - MODIFY `tests/Telescope.Tests/Program.cs` (add `Run_FileLocation_*` + `Run_IFileLocation_*`)
- **Change:** `public interface IFileLocation { string FilePath { get; } int LineNumber { get; } }` (namespace `Telescope`). `public abstract class FileLocation : IFileLocation` with `protected FileLocation(string filePath, int lineNumber) { FilePath = filePath ?? string.Empty; LineNumber = lineNumber; }` and `public string FilePath { get; }` / `public int LineNumber { get; }` — the single null-coalescing ctor the 4 hit models currently hand-roll. `public sealed class FileHit : FileLocation { public FileHit(string filePath, int lineNumber) : base(filePath, lineNumber) { } }` — the new file-finder hit model (LineNumber = 0 for plain files). The 4 hit models change their base to `FileLocation` and **delete their own `FilePath`/`LineNumber` null-coalescing ctor lines + properties** (now inherited); their public ctors and extra properties stay byte-identical: `CodeIssue(CodeIssueKind kind, string filePath, int lineNumber, string text)` + `Kind`/`Text`; `GrepHit(string filePath, int lineNumber, string lineText)` + `LineText`; `ReferenceHit(string filePath, int lineNumber, int column, bool isWrite, string symbol, string lineText)` + `Column`/`IsWrite`/`Symbol`/`LineText`; `ImplementationHit(string filePath, int lineNumber, string symbolName, string kind)` + `SymbolName`/`Kind`. **KEY DECISION:** `IFileLocation` + `FileHit` are created here (L3), not in L1 — L2's `FinderBase<FileHit>` (BP-2) needs both to exist, and the plan table's "new FileHit" (listed under L1) is folded into L3 for dependency ordering. No log literal is touched.
- **Verify-with:** NEW tests in `tests/Telescope.Tests` (RED: `FileLocation`/`IFileLocation`/`FileHit` don't exist → compile error):
  - `Run_FileLocation_NullCoalescing` — a minimal `TestHit : FileLocation` (test-local subclass); `new TestHit(null, 5)` → `FilePath == ""`, `LineNumber == 5`.
  - `Run_FileLocation_SubclassExtraFields` — `CodeIssue`/`GrepHit`/`ReferenceHit`/`ImplementationHit` still expose their extra fields and inherit `FilePath`/`LineNumber`.
  - `Run_IFileLocation_Contract` — `IFileLocation` exposes `FilePath` + `LineNumber`; all 5 hit models (`CodeIssue`, `GrepHit`, `ReferenceHit`, `ImplementationHit`, `FileHit`) implement it.
  - `Run_IFileLocation_FileHit` — `new FileHit(@"C:\p\A.cs", 0)` → `FilePath == @"C:\p\A.cs"`, `LineNumber == 0`.
  Existing hit-model tests stay green: `Run_ReferencesFinder_PayloadRoundTrips`, `Run_ImplementationFinder_PayloadRoundTrips`, `Run_GrepFinder_PayloadRoundTripsGrepHit`, `Run_Issues_OnSelectedReportsPathAndLine`. Diagnostics unchanged (no log literal touched).
- **Fails-if:** `Run_FileLocation_*`/`Run_IFileLocation_*` fail; a hit model's public ctor/property signature changes (compile error in the host gatherers `MyExtensionPackage.cs:554,664` or existing tests); `FileLocation` ctor does not null-coalesce `null` → `""`.

## BP-2 — L2: FinderBase\<THit\>

- **Files:**
  - CREATE `Telescope/FinderBase.cs`
  - MODIFY `Telescope/FileFinder.cs` (extends `FinderBase<FileHit>`; payload `string` → `FileHit`)
  - MODIFY `Telescope/CodeIssuesFinder.cs` (extends `FinderBase<CodeIssue>`)
  - MODIFY `Telescope/GrepFinder.cs` (extends `FinderBase<GrepHit>`; overrides `GetCandidates(string)`)
  - MODIFY `Telescope/ReferencesFinder.cs` (extends `FinderBase<ReferenceHit>`)
  - MODIFY `Telescope/ImplementationFinder.cs` (extends `FinderBase<ImplementationHit>`)
  - MODIFY `tests/Telescope.Tests/Program.cs` (add `Run_FinderBase_*`; update `Run_FileFinder_*` payload assertions)
- **Change:** `public abstract class FinderBase<THit> : IFinder where THit : IFileLocation` (namespace `Telescope`; `using Microsoft.VisualStudio.Shell;` for `ThreadHelper`). The base owns the common skeleton:
  - `public abstract string Name { get; }`
  - `protected abstract IReadOnlyList<THit> GatherHits();`
  - `protected abstract FinderEntry ToEntry(THit hit);`
  - `protected abstract void OpenHit(THit hit);`
  - `public virtual IReadOnlyList<FinderEntry> GetCandidates(string query = "")` — `AssertUiThread(); try { hits = GatherHits() ?? Array.Empty<THit>(); } catch (Exception ex) { Debug.WriteLine(GatherErrorLiteral(ex)); hits = Array.Empty<THit>(); } return hits.Select(ToEntry).ToList();` (the query is ignored by non-query finders — they gather once).
  - `public void OnSelected(FinderEntry entry)` — `if (entry.Payload is not THit hit) return; AssertUiThread(); try { OpenHit(hit); } catch (Exception ex) { TelescopeLog.Log($"open {OpenErrorNoun} failed: {ex.Message}"); Debug.WriteLine($"{Telescope.DiagnosticLog.Telescope}{FinderNameForErrors} failed to open '{entry.Display}': {ex.Message}"); }`. (**m4:** use `TelescopeLog.Log` — created in Lane 1 BP-6 — not `NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}...`; the prefix is added by the helper, text byte-identical.)
  - `protected void AssertUiThread()` — `if (ThreadHelper.JoinableTaskContext != null) ThreadHelper.ThrowIfNotOnUIThread();` (the References/Implementation guarded variant; a no-op in the unit-test host).
  - Error-literal hooks (preserve each finder's **byte-identical** `Debug.WriteLine`/`NeoVisualLog.Log` error text): `protected virtual string GatherErrorLiteral(Exception ex) => $"{Telescope.DiagnosticLog.Telescope}{GetType().Name} failed to enumerate: {ex.Message}";`, `protected virtual string OpenErrorNoun => "item";`, `protected virtual string FinderNameForErrors => GetType().Name;`.
  Subclass wiring (public ctors unchanged — package registration at `MyExtensionPackage.cs:71-79` untouched):
  - `FileFinder : FinderBase<FileHit>` — `GatherHits()` = the DTE enumeration (test seam `_testCandidateSource` branch returns `FileHit(path, 0)` list); `ToEntry(FileHit)` = `new FinderEntry(Path.GetFileName(hit.FilePath), hit)` (payload `string` → `FileHit`); `OpenHit(FileHit)` = test seam `_testOpener(hit.FilePath)` + `[Telescope] opened file: {path}` log, else DTE `OpenFile` + same log; `OpenErrorNoun => "file"`.
  - `CodeIssuesFinder : FinderBase<CodeIssue>` — `GatherHits()` = TODO scan + Error List (test seam branch); `ToEntry`/`OpenHit` as today (**M1:** `OpenHit` = `File.Exists` guard → `DteFileOpener.OpenAtLine(dte, hit.FilePath, hit.LineNumber)` → `[Telescope] opened issue: {filePath} line={lineNumber}` — `GotoLine` was deleted in Lane 1 BP-2; the `[Telescope] goto line={line}` diagnostic is emitted inside `DteFileOpener.OpenAtLine`); `OpenErrorNoun => "issue"`.
  - `GrepFinder : FinderBase<GrepHit>` — **overrides** `GetCandidates(string query)` (query-driven: empty query → empty, else scan + `[Telescope] grep hits={count}`), keeping its own try/catch + `HitCap`; uses the base `OnSelected`; `OpenHit(GrepHit)` = test seam `_testOpener(hit)`, else `DteFileOpener.OpenAtLine(dte, hit.FilePath, hit.LineNumber)` → `[Telescope] opened grep: file={filePath} line={lineNumber}` (**M1:** `GotoLine` was deleted in Lane 1 BP-2); `OpenErrorNoun => "grep"`.
  - `ReferencesFinder : FinderBase<ReferenceHit>` — `GatherHits()` = `_gatherer() ?? Array.Empty<ReferenceHit>()`; `OpenHit(ReferenceHit)` = `_opener(hit)` + `[Telescope] opened reference: file={filePath} line={lineNumber} col={column} access=read|write`; `OpenErrorNoun => "reference"`; `GatherErrorLiteral(ex) => $"{Telescope.DiagnosticLog.Telescope}references gather failed: {ex.Message}"`.
  - `ImplementationFinder : FinderBase<ImplementationHit>` — `GatherHits()` = `_gatherer() ?? Array.Empty<ImplementationHit>()`; `OpenHit(ImplementationHit)` = `_opener(hit)` + `[Telescope] opened implementation: file={filePath} line={lineNumber}`; `OpenErrorNoun => "implementation"`; `GatherErrorLiteral(ex) => $"{Telescope.DiagnosticLog.Telescope}implementations gather failed: {ex.Message}"`.
  The host-injected gatherers/opener seams for References/Implementation are preserved (they live in the subclasses' `GatherHits`/`OpenHit`), so the finders stay hermetic-testable. The `[Telescope] references gathered reads={reads} writes={writes}` / `[Telescope] implementations gathered count={count}` summary logs move into the subclasses' `GatherHits` (byte-identical).
- **Verify-with:** NEW tests in `tests/Telescope.Tests` (RED: `FinderBase<THit>` doesn't exist → compile error), using a test-local `TestHit : FileLocation` + a minimal `TestFinder : FinderBase<TestHit>`:
  - `Run_FinderBase_GetCandidatesMapsGather` — gather returns 2 hits → `GetCandidates()` returns 2 entries with the exact payloads.
  - `Run_FinderBase_OnSelectedOpensHit` — `OnSelected` invokes `OpenHit` with the payload.
  - `Run_FinderBase_GatherErrorSwallowed` — a throwing `GatherHits` → `GetCandidates()` returns empty (no throw).
  - `Run_FinderBase_OpenErrorSwallowed` — a throwing `OpenHit` → `OnSelected` does not throw.
  - `Run_FinderBase_NonMatchingPayloadIgnored` — `OnSelected` with a non-`THit` payload is a no-op.
  Existing finder tests stay green after the `Run_FileFinder_*` payload updates (`Run_FileFinder_EnumeratesCandidates` asserts `(entries[0].Payload as FileHit)?.FilePath == a`; `Run_FileFinder_OpenSelectedCallsOpener`/`Run_FileFinder_OpenMissingFileIsNoOp` pass `new FinderEntry(..., new FileHit(path, 0))`). Diagnostics unchanged: `[Telescope] opened file: {path}`, `[Telescope] opened issue: {filePath} line={lineNumber}`, `[Telescope] grep hits={count}`, `[Telescope] references gathered reads={reads} writes={writes}`, `[Telescope] implementations gathered count={count}`, `[Telescope] opened reference: file={filePath} line={lineNumber} col={column} access=read|write`, `[Telescope] opened implementation: file={filePath} line={lineNumber}`.
- **Fails-if:** `Run_FinderBase_*` fails; a finder's public ctor signature changes (compile error at `MyExtensionPackage.cs:71-79`); a `[Telescope]` structured-log literal changes (`git diff`); a finder's test seam is lost (hermetic tests no longer build/run); `FileFinder` payload no longer round-trips as `FileHit`.

## BP-3 — L1: PreviewRenderer

- **Files:**
  - CREATE `Telescope/PreviewRenderer.cs`
  - MODIFY `Telescope/TelescopeOverlay.cs` (collapse `LoadPreviewForSelection` :433-542 to ONE `payload is IFileLocation` branch; `SetPreviewContent`/`ApplyPreviewCaret` delegate to `PreviewRenderer`)
  - MODIFY `tests/Telescope.Tests/Program.cs` (no new RED — `Run_IFileLocation_*` added in BP-1)
- **Change:** `internal static class PreviewRenderer` (namespace `Telescope`):
  - `public static void Show(RichTextBox previewBox, TextMotionNavigator navigator, IFileLocation location)` — `if (!File.Exists(location.FilePath)) { SetContent(previewBox, navigator, string.Empty); return; }` then `try { string content = File.ReadAllText(location.FilePath); SetContent(previewBox, navigator, content); if (location.LineNumber > 0) { navigator.MoveToLine(location.LineNumber); ApplyCaret(previewBox, navigator); NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview caret={navigator.Caret} line={navigator.LineNumber}"); } NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview file={location.FilePath} chars={content.Length}"); } catch (Exception ex) { NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview load failed: {ex.Message}"); }`.
  - `public static void SetContent(RichTextBox previewBox, TextMotionNavigator navigator, string content)` — the current `SetPreviewContent` body (`TelescopeOverlay.cs:741-781`): `navigator.SetText(content)` + the `FlowDocument`/`SyntaxHighlighter.Segment` tokenize + `[Telescope] preview tokens={segments.Count}` log.
  - `public static void ApplyCaret(RichTextBox previewBox, TextMotionNavigator navigator)` — the current `ApplyPreviewCaret` body (`TelescopeOverlay.cs:721-734`): caret position + scroll.
  The overlay's `SetPreviewContent(string)`/`ApplyPreviewCaret()` become thin wrappers delegating to `PreviewRenderer.SetContent(_previewBox, _previewNavigator, content)` / `PreviewRenderer.ApplyCaret(_previewBox, _previewNavigator)` (the overlay still needs them for its empty-preview and motion-caret uses at :437, :541, :623, :850). `LoadPreviewForSelection` collapses the 5 branches (:442-539) to: `if (payload is IFileLocation location) { PreviewRenderer.Show(_previewBox, _previewNavigator, location); return; } SetPreviewContent(string.Empty);`. The renderer is VS-coupled (RichTextBox/FlowDocument) — e2e-gated; only the `IFileLocation` contract is unit-testable.
- **Verify-with:** `Run_IFileLocation_*` (added in BP-1, GREEN by BP-3) + existing `Run_TextMotionNavigator_*` MoveToLine tests stay green. Diagnostics preserved byte-identically: `[Telescope] preview file={filePath} chars={length}`, `[Telescope] preview caret={caret} line={lineNumber}`, `[Telescope] preview tokens={segmentCount}`, `[Telescope] preview load failed: {ex.Message}` (log order per branch unchanged: tokens → caret → file).
- **Fails-if:** a `[Telescope] preview ...` literal changes (`git diff`); the preview caret no longer jumps to `LineNumber` for a hit (regression in `MoveToLine` wiring); a non-`IFileLocation` payload no longer clears the preview; `Run_IFileLocation_*` fails.

## BP-4 — C5: RoslynCaretContext

- **Files:**
  - MODIFY `MyExtension/MyExtensionPackage.cs` (extract `TryGetCaretSymbol`; `GatherReferences`/`GatherImplementations` call it)
- **Change:** add `private bool TryGetCaretSymbol(out VisualStudioWorkspace workspace, out Document document, out ISymbol symbol)` to `MyExtensionPackage` — the shared prologue of `GatherReferences` (`:488-536`) and `GatherImplementations` (`:570-612`): `ThreadHelper.ThrowIfNotOnUIThread();` → resolve DTE via **`VsServices.Dte(this)`** + `ActiveDocument` → MEF `VisualStudioWorkspace` via **`VsServices.Mef<VisualStudioWorkspace>(this)`** (**M2:** the raw `SComponentModel`/`GetDTE` prologue was consolidated into `VsServices` in Lane 1 BP-5 — do NOT re-introduce it) → `CurrentSolution` → `GetDocumentIdsWithFilePath(active.FullName).FirstOrDefault()` → `GetDocument` → `GetCaretOffset(dte, active, document)` → `GetSemanticModelAsync` + `FindSymbolAtPositionAsync` (both inside `ThreadHelper.JoinableTaskFactory.Run` — never `.Result` on the UI thread) → `return symbol != null;` (all `out` params nulled on early return). **The unused `GetSyntaxRootAsync` call (`:527-528` and `:607-608`) is dropped** — its result was never read in either gatherer. `GatherReferences` becomes `if (!TryGetCaretSymbol(out var workspace, out var document, out var symbol)) return Array.Empty<ReferenceHit>();` then diverges at `FindReferencesAsync(symbol, workspace.CurrentSolution)`; `GatherImplementations` likewise diverges at `FindImplementationsAsync`. The hit-building loops (`:541-557`, `:621-675`) are unchanged.
- **Verify-with:** `dotnet build`; both unit suites stay green. No new unit tests (VS-coupled). Diagnostics unchanged: `[Telescope] references gathered reads={reads} writes={writes}`, `[Telescope] implementations gathered count={count}`.
- **Fails-if:** `TryGetCaretSymbol` returns true with a null `symbol`/`document`/`workspace` (gatherer NRE); a Roslyn async call is changed to `.Result`/`.GetAwaiter().GetResult()` on the UI thread (deadlock); `[Telescope] references gathered ...` / `[Telescope] implementations gathered count=...` no longer emitted; the references/implementation hit lists change (behavior drift).

## BP-5 — C7: ControllerRegistry

- **Files:**
  - MODIFY `MyExtension/WindowManager.cs` (`GetController` → pure `TryGetValue` + `_defaultController` fallback)
  - MODIFY `MyExtension/MyExtensionPackage.cs` (register ALL controllers explicitly in `InitializeAsync`)
- **Change:** in `MyExtensionPackage.InitializeAsync`, after `_windowManager = new WindowManager(monitorSelection)` (`:84`), keep the explicit `_windowManager.RegisterController(new SolutionExplorerController(...))` (`:85`) and add an explicit registration loop over every `ToolWindowType` enum value except `Unknown`: `foreach (ToolWindowType type in Enum.GetValues(typeof(ToolWindowType))) { if (type == ToolWindowType.Unknown) continue; _windowManager.RegisterController(GeneralToolWindowController.IsTextInputType(type) ? new TextInputToolWindowController(type) : new GeneralToolWindowController(type)); }` — the implicit lazy branches (`WindowManager.cs:89-98`) move into `InitializeAsync` as explicit registrations. `WindowManager.GetController` (`:74-99`) collapses to `return _controllers.TryGetValue(type, out var registered) ? registered : _defaultController;` (the `_defaultController` = `GeneralToolWindowController(ToolWindowType.Unknown)` stays as the Unknown fallback). Both controller ctors are trivial (a `ToolWindowType` + a bool), so eager creation of ~60 lightweight instances at init is safe and behavior-preserving (mode is still remembered per window type — one instance per type). `RegisterController` (`:68-72`) is unchanged.
- **Verify-with:** `dotnet build`; both unit suites stay green. No new unit tests (VS-coupled). Diagnostics unchanged: `[NeoVisual] toolwindow-move key={key} -> arrow vk={vk}`, `[NeoVisual] toolwindow-enter-input` / `toolwindow-exit-input`, `[NeoVisual] solution-explorer ...`.
- **Fails-if:** `GetController` returns null for a registered type (dispatch regression); a text-input type gets a `GeneralToolWindowController` instead of `TextInputToolWindowController` (initial-mode regression — text-input surfaces must start in insert mode); `_defaultController` no longer returned for `ToolWindowType.Unknown`; a `[NeoVisual] toolwindow-*` / `[NeoVisual] solution-explorer ...` literal changes.

---

## Unit tests (RED proof)

All new seam tests follow the `OverlayKeyHandler`/`TextMotionNavigator` pattern: dependency-free classes the UI delegates to. **RED proof** = the test references an API that does not exist yet (compile error); GREEN after the merge.

| Test (add to `tests/Telescope.Tests/Program.cs`) | BP | RED proof | GREEN assertion |
|--------------------------------------------------|----|-----------|-----------------|
| `Run_FileLocation_NullCoalescing` | BP-1 | `FileLocation` doesn't exist → compile error | `new TestHit(null, 5)` → `FilePath == ""`, `LineNumber == 5` |
| `Run_FileLocation_SubclassExtraFields` | BP-1 | `FileLocation` doesn't exist → compile error | `CodeIssue`/`GrepHit`/`ReferenceHit`/`ImplementationHit` expose extra fields + inherit `FilePath`/`LineNumber` |
| `Run_IFileLocation_Contract` | BP-1 | `IFileLocation` doesn't exist → compile error | all 5 hit models implement `IFileLocation` (`FilePath` + `LineNumber`) |
| `Run_IFileLocation_FileHit` | BP-1 | `FileHit` doesn't exist → compile error | `new FileHit(@"C:\p\A.cs", 0)` → `FilePath == @"C:\p\A.cs"`, `LineNumber == 0` |
| `Run_FinderBase_GetCandidatesMapsGather` | BP-2 | `FinderBase<THit>` doesn't exist → compile error | `GetCandidates()` maps `GatherHits()` via `ToEntry` (2 hits → 2 entries, exact payloads) |
| `Run_FinderBase_OnSelectedOpensHit` | BP-2 | `FinderBase<THit>` doesn't exist → compile error | `OnSelected` invokes `OpenHit` with the payload |
| `Run_FinderBase_GatherErrorSwallowed` | BP-2 | `FinderBase<THit>` doesn't exist → compile error | throwing `GatherHits` → empty result, no throw |
| `Run_FinderBase_OpenErrorSwallowed` | BP-2 | `FinderBase<THit>` doesn't exist → compile error | throwing `OpenHit` → no throw |
| `Run_FinderBase_NonMatchingPayloadIgnored` | BP-2 | `FinderBase<THit>` doesn't exist → compile error | non-`THit` payload → no-op |
| UPDATE `Run_FileFinder_EnumeratesCandidates` | BP-2 | payload `string` → `FileHit` (assertion fails on the old code) | `(entries[0].Payload as FileHit)?.FilePath == a` |
| UPDATE `Run_FileFinder_OpenSelectedCallsOpener` | BP-2 | payload `string` → `FileHit` (assertion fails on the old code) | `OnSelected(new FinderEntry("Alpha.cs", new FileHit(a, 0)))` opens `a` |
| UPDATE `Run_FileFinder_OpenMissingFileIsNoOp` | BP-2 | payload `string` → `FileHit` (assertion fails on the old code) | `OnSelected(new FinderEntry("Ghost.cs", new FileHit(missing, 0)))` → 0 opens |

Existing tests that must stay green (no change): `Run_ReferencesFinder_*`, `Run_ImplementationFinder_*`, `Run_GrepFinder_*`, `Run_Issues_*`, `Run_TextMotionNavigator_*`, `Run_LogPrefixes_Pinned`, and the full `tests/NeoVisual.Tests` suite (58, after Lanes 1-2).

---

## Verification Trace

| failing test / scenario | implicated steps | expected diagnostic / pass signal |
|---|---|---|
| `Run_FileLocation_*` (RED: `FileLocation` doesn't exist) | BP-1 | `FileLocation` null-coalesces `FilePath`; subclass extra fields intact |
| `Run_IFileLocation_*` (RED: `IFileLocation`/`FileHit` don't exist) | BP-1, BP-3 | all 5 hit models implement `IFileLocation`; `FileHit` carries `FilePath`+`LineNumber` |
| `Run_FinderBase_*` (RED: `FinderBase<THit>` doesn't exist) | BP-2 | base owns GetCandidates/OnSelected/AssertUiThread/error-handling; a minimal `THit : FileLocation` subclass works |
| `Run_FileFinder_*` (payload `string` → `FileHit`) | BP-2 | `[Telescope] opened file: {path}` unchanged; payload round-trips as `FileHit` |
| `Run_ReferencesFinder_*` / `Run_ImplementationFinder_*` / `Run_GrepFinder_*` / `Run_Issues_*` | BP-1, BP-2 | `[Telescope] references gathered reads={reads} writes={writes}`, `[Telescope] implementations gathered count={count}`, `[Telescope] grep hits={count}`, `[Telescope] opened issue: {filePath} line={lineNumber}` unchanged |
| `Run_TextMotionNavigator_*` (MoveToLine) | BP-3 | `[Telescope] preview caret={caret} line={lineNumber}` unchanged; `MoveToLine` still lands on the hit line |
| `tests/Telescope.Tests` (63) | BP-1..BP-5 | all existing `Run_*` pass |
| `tests/NeoVisual.Tests` (58) | BP-1..BP-5 | all existing `Run_*` pass |
| A3 log-literal check (`git diff`) | BP-1..BP-5 | no `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal changed |

**Known-RED allowlist (do NOT report as regressions):** none — `docs/progress.md` reports no known-RED e2e scenario remains (all 35 green as of 2026-09-27); e2e is deferred to `e2e-queue.md` anyway.

---

## E2E gate (deferred — informational only)

No NEW e2e scenarios are needed — every merge is behavior-preserving, so the e2e gate is the **existing** finder/preview scenarios staying green on a capable machine (queued in `e2e-queue.md`, NOT executed here): the `telescope-*` finder scenarios (open-file/issues/grep/references/implementation/preview) for BP-1..BP-3, `telescope-references`/`telescope-implementation` for BP-4, and the tool-window/explorer scenarios for BP-5. Each asserts: the listed scenarios stay GREEN + `git diff` shows no log-literal drift. Diagnostics depended on: the existing `[Telescope]`/`[NeoVisual]` lines (unchanged).

---

## KEY DECISIONS (do not second-guess)

- **`IFileLocation` + `FileHit` are created in BP-1 (L3), not L1.** L2's `FinderBase<FileHit>` (BP-2) needs both to exist; the plan table's "new FileHit" (listed under L1) is folded into L3 for dependency ordering. Consequently `Run_IFileLocation_*` is added in BP-1 (RED: interface doesn't exist) and BP-3's Verify-with references it as the contract the collapsed preview branch relies on.
- **`FinderBase.GetCandidates` is `virtual` and GrepFinder overrides it.** The base's no-arg `GatherHits()` cannot express query-driven grep; GrepFinder keeps its own `GetCandidates(string query)` (empty-query → empty, `HitCap`, `[Telescope] grep hits={count}`) and uses the base's `OnSelected`. The other 4 finders use the base `GetCandidates` (query ignored).
- **Error literals are preserved byte-identically via protected hooks** (`GatherErrorLiteral`/`OpenErrorNoun`/`FinderNameForErrors`), not collapsed — the References/Implementation gather errors (`references gather failed:` / `implementations gather failed:`) differ structurally from the DTE trio's (`... failed to enumerate:`), and M-M7 forbids any log-literal change.
- **FileFinder's payload changes from `string` to `FileHit`** (part of BP-2). This is the one intentional public-contract change in the lane; the 3 `Run_FileFinder_*` tests are updated in the same commit (A4). The overlay's `string path` preview branch (dead after BP-2) is removed by BP-3.
- **C7 registers ~60 lightweight controllers eagerly at init** (one per `ToolWindowType` except `Unknown`); `GetController` becomes a pure `TryGetValue` + `_defaultController` fallback. Mode is still remembered per window type (one instance per type). `_defaultController` (`GeneralToolWindowController(Unknown)`) stays as the Unknown fallback.
- **C5 drops the unused `GetSyntaxRootAsync` call** in both gatherers — its result was never read; all remaining Roslyn async calls stay inside `ThreadHelper.JoinableTaskFactory.Run` (never `.Result` on the UI thread).


---

# Lane 4 — Navigation cluster (highest-value testability win)

> Self-contained Lane 4 section of the architecture-consolidation plan (`plans/plan.md` §4).
> Executed top-to-bottom by the build-agent in the order **N2 → N4 → N3 → N1 → matrix-rebuild cache**
> (BP-1..BP-5, one BP step per merge). Every merge is behavior-preserving.
>
> **Gate:** both offline unit suites stay green (`dotnet run --project tests/Telescope.Tests` → 72,
> `tests/NeoVisual.Tests` → 58 — after Lanes 1-3); the new seam tests (BP-1, BP-2, BP-3) are RED before the merge and
> GREEN after; `pwsh tools/check-doc-refs.ps1` passes after the doc-ref update steps (BP-2, BP-3, BP-4);
> `git diff` shows no change to any `[Telescope]`/`[NeoVisual]`/`[Hook]` log literal (M-M7 NOT triggered).
> e2e is deferred to `e2e-queue.md` — **no e2e scenario name appears in any Verify-with**; the e2e gate
> column is informational only.
>
> **Known-RED allowlist:** none — `docs/progress.md` reports no known-RED e2e scenario remains (all 35
> green as of 2026-09-27); e2e is deferred anyway.

## Merges table

| id | Merge | Current state (file:line, verified against source) | Target (exact API) | Blast radius | Coverage (unit tests) | e2e gate (deferred, informational) |
|----|-------|---------------------------------------------------|--------------------|--------------|------------------------|-----------------------------------|
| N2 | **RectCoordinate** | `class RectCoordinate` with mutable public fields `x,y,width,height` (`MyExtension/CardinalMovment/RectCoordinate.cs:10-25`; fields :12-15, ctor :17-23); ~12 inline `x+width`/`y+height` (`WindowMatrix.cs:213,216,227,230,255,262,269,277,306,314,322,330`); `IsEmpty` check (`WindowMatrix.cs:415-418`); `AdjacencySize` (`WindowMatrix.cs:141-147`); test `Run_RectCoordinate_StoresFields` (`tests/NeoVisual.Tests/Program.cs:489-496`) asserts the lowercase fields | `readonly struct RectCoordinate { public readonly int X,Y,Width,Height; public RectCoordinate(int x,int y,int width,int height); public int Right => X+Width; public int Bottom => Y+Height; public bool IsEmpty; public int Adjacency(RectCoordinate other, Axis axis); public int GapTo(RectCoordinate other, Direction direction); }` — every engine filter becomes `candidate.Adjacency(active, direction.Axis())` | WindowMatrix (mechanical field rename), WindowControlAdapter (deleted in N1), the test | **UPDATE** `Run_RectCoordinate_StoresFields` for readonly uppercase fields + **NEW** `Run_RectCoordinate_Right_Bottom` / `Run_RectCoordinate_IsEmpty` / `Run_RectCoordinate_Adjacency` / `Run_RectCoordinate_GapTo` (RED: members don't exist) | neovisual-window-nav + new unit tests |
| N4 | **NavigationSettings** | `CardinalNavigationConstants.cs:10-19` — 4 dead constants `DefaultLogicalYWindowDivide` (:10), `DOCUMENT` (:16), `TOOL` (:17), `GithubMessage` (:19); 3 live divide constants `DefaultLogicalXWindowDivide` (:11), `DefaultLogicalTabPaneDivide` (:12), `DefaultLogicalSelectorScale` (:14); `SetWindowDivideSelectionSizes` (`WindowMatrix.cs:101-115`) is the only DPI logic | `sealed class NavigationSettings { public int XDivide { get; } public int YDivide { get; } public static NavigationSettings FromSystemDpi(); public static NavigationSettings FromDpi(int systemDpiX, int systemDpiY); }` injected into the engine; delete the 4 dead constants | WindowMatrix only | **NEW** `Run_NavigationSettings_FromDpi` (RED: class doesn't exist) | neovisual-window-nav |
| N3 | **NavigationEngine** | 5 direction-parameterized filter methods with if/else direction branches (**CORRECTION:** NO switch statements — the doc's "5× 4-way switches" is inaccurate) at `WindowMatrix.cs:155-409` (`SortByLargestAdjacency` :155-193, `RemoveWindowsNotAligned` :200-239, `RemoveWindowsNotAdjacent` :245-283 [dead], `RemoveWindowsByClosestAdjacency` :289-355, `RemoveWindowsInWrongDirection` :361-409) + hardcoded pipeline (`:435-441`: `RemoveHiddenOrTabbedWindows` :435, `RemoveWindowsInWrongDirection` :436, `RemoveWindowsNotAligned` :437, `RemoveWindowsByClosestAdjacency` :439, `SortByLargestAdjacency` :441 — `RemoveWindowsByClosestAdjacency` re-runs two filters at :294-295, M8); unit-testing impossible (needs AsyncPackage+DTE+IVsUIShell) | `enum Direction { Up, Down, Left, Right }` + `enum Axis { X, Y }` + `static class DirectionExtensions { Axis Axis(this Direction); int Sign(this Direction); }` + `sealed class WindowNavigationEngine { static int? SelectTarget(RectCoordinate active, IReadOnlyList<RectCoordinate> candidates, Direction direction, NavigationSettings settings); }` running a `List<Func<RectCoordinate,RectCoordinate,Direction,bool>>` pipeline as ONE O(n) pass (two linear scans, no 5 list-mutations + sort; deepens M8). `WindowMatrix` becomes a thin COM shell. **Highest-value testability win in the repo.** | WindowMatrix only | **NEW** `Run_WindowNavigationEngine_*` (RED: class doesn't exist) — the `OverlayKeyHandler` pattern | new unit tests + neovisual-window-nav |
| N1 | **WindowAdapter** | `IVsFrameView.cs:51-127` 13 zero-caller forwards (Show :51, Hide :57, IsVisible :63, ShowNoActivate :69, CloseFrame :75, SetFramePos :81, GetFramePos :87, GetProperty :93, SetProperty :99, GetGuidProperty :105, SetGuidProperty :111, QueryViewInterface :117, IsOnScreen :123) + dead `internalFrame` (:22) + dead `IsTabbedAndInvisible` (:43) + `IVsWindowFrameNotify` `NotImplementedException` landmine (:129-147); `WindowControlAdapter.cs` (188 lines) + `IVsUIWindowFrameExtractor.cs` (65 lines) complete the trio (~422 lines → 1 type) | one `sealed class WindowAdapter { IVsWindowFrame _frame; EnvDTE.Window _dte; RectCoordinate _rect; RectCoordinate Rect; EnvDTE.Window DteWindow; void Activate(); bool AutoHides(); bool IsOnScreen(); static List<WindowAdapter> Enumerate(AsyncPackage); static WindowAdapter? FindActive(EnvDTE.Window, IEnumerable<WindowAdapter>); static IEnumerable<WindowAdapter> LinkedTo(EnvDTE.Window, IEnumerable<WindowAdapter>); }` | WindowMatrix ctor only | none (VS-coupled) | neovisual-window-nav + neovisual-toolwindow |
| — | **Matrix-rebuild cache** | every Ctrl+H/J/K/L rebuilds the whole matrix: ctor enumerates ALL frames (`IVsUIWindowFrameExtractor.GetIVsWindowFramesEnumerator`), wraps each in `GetWindowScreenRect` COM (`WindowControlAdapter` ctor), pairs via `GetWindowObject`, O(n²) `CompareWindows` — all on the UI thread per keystroke (`InputHandler.Navigate`, `InputHandler.cs:457-458`) | cache a `List<WindowAdapter>` in `WindowManager` (`WindowManager.cs`); refresh rects lazily (active + candidates only) on window events; at minimum reuse the `IVsUIShell` enumeration across keystrokes | WindowManager + WindowMatrix + InputHandler.Navigate call site | none (perf, behavior-preserving) | neovisual-window-nav |

---

## BP-1 — N2: RectCoordinate (readonly struct + geometry members)

- **Files:**
  - MODIFY `MyExtension/CardinalMovment/RectCoordinate.cs` (`class` → `readonly struct`; lowercase mutable fields `x,y,width,height` :12-15 → `public readonly int X,Y,Width,Height`; keep the 4-arg ctor :17-23; add `Right`, `Bottom`, `IsEmpty`, `Adjacency`, `GapTo`)
  - CREATE `MyExtension/CardinalMovment/Direction.cs` (`enum Axis { X, Y }` + `enum Direction { Up, Down, Left, Right }` — required by the `Adjacency(RectCoordinate, Axis)` / `GapTo(RectCoordinate, Direction)` signatures; the `Axis()/Sign()` extensions land in BP-3)
  - MODIFY `MyExtension/CardinalMovment/WindowMatrix.cs` (mechanical rename of every `coordinates.x/y/width/height` access to `coordinates.X/Y/Width/Height` — the filter methods are deleted in BP-3, so this is a compile-preserving rename only: :163-168, :178-183, :212-216, :226-230, :255, :262, :269, :277, :306, :314, :322, :330, :376, :385, :393, :401, :415-418)
  - MODIFY `tests/NeoVisual.Tests/Program.cs` (update `Run_RectCoordinate_StoresFields` :489-496 to the uppercase fields; add the new `Run_RectCoordinate_*` tests)
- **Change:** `readonly struct RectCoordinate` (namespace `CardinalNavigation`) with `public readonly int X, Y, Width, Height`, ctor `RectCoordinate(int x, int y, int width, int height)`, and:
  - `public int Right => X + Width;` / `public int Bottom => Y + Height;`
  - `public bool IsEmpty => X == 0 && Y == 0 && Width == 0 && Height == 0;` (the `RemoveHiddenOrTabbedWindows` predicate, `WindowMatrix.cs:415-418`)
  - `public int Adjacency(RectCoordinate other, Axis axis)` — the closed-form 1-D span overlap from `AdjacencySize` (`WindowMatrix.cs:141-147`) generalized to an axis: for `Axis.X` overlap `[X, X+Width)` with `[other.X, other.X+other.Width)`; for `Axis.Y` overlap `[Y, Y+Height)` with `[other.Y, other.Y+other.Height)`; `Math.Max(0, end - start)`.
  - `public int GapTo(RectCoordinate other, Direction direction)` — the distance from `this` to `other` in the given direction (positive when `other` is in that direction from `this`), matching the current distance functions exactly: `Up → other.Y - Bottom`, `Down → Y - other.Bottom`, `Left → other.X - Right`, `Right → X - other.Right`.
  - `WindowMatrix.cs` gets the mechanical lowercase→uppercase field rename only (no logic change; the filter methods are deleted in BP-3).
- **Verify-with:** NEW tests in `tests/NeoVisual.Tests` (RED: `Right`/`Bottom`/`IsEmpty`/`Adjacency`/`GapTo`/`Axis`/`Direction` don't exist → compile error; the updated `Run_RectCoordinate_StoresFields` referencing `r.X` also fails to compile before the merge):
  - `Run_RectCoordinate_StoresFields` (updated): `new RectCoordinate(1,2,3,4)` → `X==1, Y==2, Width==3, Height==4`.
  - `Run_RectCoordinate_Right_Bottom`: `new RectCoordinate(1,2,3,4).Right == 4`, `.Bottom == 6`.
  - `Run_RectCoordinate_IsEmpty`: `new RectCoordinate(0,0,0,0).IsEmpty == true`; `new RectCoordinate(1,0,0,0).IsEmpty == false`.
  - `Run_RectCoordinate_Adjacency`: `new RectCoordinate(0,0,10,10).Adjacency(new RectCoordinate(5,0,10,10), Axis.X) == 5`; no-overlap `(20,0,10,10)` → 0; `Axis.Y` `(0,5,10,10)` → 5.
  - `Run_RectCoordinate_GapTo`: `new RectCoordinate(100,0,100,50).GapTo(new RectCoordinate(100,100,100,100), Direction.Up) == 50`; `(100,202,100,50).GapTo(active, Direction.Down) == 2`; `(0,100,50,100).GapTo(active, Direction.Left) == 50`; `(250,100,50,100).GapTo(active, Direction.Right) == 50`.
  - `dotnet run --project tests/NeoVisual.Tests` → 38 + new tests green. Diagnostic unchanged: `[NeoVisual] navigate direction={direction}` (`InputHandler.cs:453`, byte-identical).
- **Fails-if:** `Run_RectCoordinate_*` fails; `WindowMatrix.cs` no longer compiles (a lowercase `.x/.y/.width/.height` access remains); `Right`/`Bottom`/`Adjacency`/`GapTo` return values that differ from the current inline math (behavior drift).

## BP-2 — N4: NavigationSettings (DPI divide, one source of truth)

- **Files:**
  - CREATE `MyExtension/CardinalMovment/NavigationSettings.cs`
  - MODIFY `MyExtension/CardinalMovment/CardinalNavigationConstants.cs` (delete the 4 dead constants `DefaultLogicalYWindowDivide` :10, `DOCUMENT` :16, `TOOL` :17, `GithubMessage` :19; keep `LEFT/RIGHT/UP/DOWN` :5-8 + the 3 live divide constants :11, :12, :14)
  - MODIFY `MyExtension/CardinalMovment/WindowMatrix.cs` (delete `SetWindowDivideSelectionSizes` :101-115 + the DPI fields `m_DeviceDpiX/Y` :21-22, `m_DpiXScale/YScale` :24-25, `m_XDivide/m_YDivide` :27-28; replace with `_settings = NavigationSettings.FromSystemDpi();` in the ctor)
  - MODIFY `tests/NeoVisual.Tests/Program.cs` (add `Run_NavigationSettings_FromDpi`)
  - MODIFY docs (doc-ref, required — `DefaultLogicalYWindowDivide` + `SetWindowDivideSelectionSizes` become un-greppable): `docs/progress.md:1300` (n2) AND `.opencode/skills/vs-extension-dev/SKILL.md:113` (the constants list at `SKILL.md:112-114` backticks `DefaultLogicalYWindowDivide`; SKILL.md IS lint-scanned) AND **`SKILL.md:109`** (backticks `SetWindowDivideSelectionSizes`, which BP-2 deletes from `WindowMatrix.cs` — reword the DPI paragraph to reference `NavigationSettings.FromSystemDpi()`) — reword to "deleted in N4" / drop the deleted constant (keep `DefaultLogicalXWindowDivide`/`DefaultLogicalTabPaneDivide`/`DefaultLogicalSelectorScale`) so `pwsh tools/check-doc-refs.ps1` passes.
- **Change:** `sealed class NavigationSettings` (namespace `CardinalNavigation`):
  - `public int XDivide { get; }` / `public int YDivide { get; }` (private ctor).
  - `public static NavigationSettings FromSystemDpi() => FromDpi(DpiAwareness.SystemDpiX, DpiAwareness.SystemDpiY);` — the only VS-coupled entry point.
  - `public static NavigationSettings FromDpi(int systemDpiX, int systemDpiY)` — the **pure, testable** divide math, byte-identical to `SetWindowDivideSelectionSizes`: `XDivide = (int)(CardinalNavigationConstants.DefaultLogicalXWindowDivide * (systemDpiX / (double)DpiAwareness.DefaultLogicalDpi) * CardinalNavigationConstants.DefaultLogicalSelectorScale)`; `YDivide = (int)(CardinalNavigationConstants.DefaultLogicalTabPaneDivide * (systemDpiY / (double)DpiAwareness.DefaultLogicalDpi) * CardinalNavigationConstants.DefaultLogicalSelectorScale)`. (For every real system DPI the result is an exact integer — `XDivide = SystemDpiX/4`, `YDivide = 25*SystemDpiY/24` — so the `int` truncation is behavior-preserving.)
  - Delete the 4 dead constants; the 3 live divide constants stay in `CardinalNavigationConstants` (referenced by `FromDpi` — keeps `SKILL.md:112` doc-ref resolvable).
- **Verify-with:** NEW test `Run_NavigationSettings_FromDpi` in `tests/NeoVisual.Tests` (RED: `NavigationSettings` doesn't exist → compile error): `NavigationSettings.FromDpi(96,96)` → `XDivide==24, YDivide==100`; `FromDpi(144,144)` → `36, 150`; `FromDpi(120,120)` → `30, 125`. `dotnet run --project tests/NeoVisual.Tests` green. `pwsh tools/check-doc-refs.ps1` exits 0 (after the `docs/progress.md:1300` update). Diagnostic unchanged: `[NeoVisual] navigate direction={direction}`.
- **Fails-if:** `Run_NavigationSettings_FromDpi` fails; `FromDpi` produces a divide that differs from `SetWindowDivideSelectionSizes` for a real DPI (behavior drift); a dead constant is still referenced anywhere; doc-ref lint fails on `DefaultLogicalYWindowDivide`.

## BP-3 — N3: NavigationEngine (the pure seam — core algorithm becomes unit-testable)

- **Files:**
  - CREATE `MyExtension/CardinalMovment/WindowNavigationEngine.cs` (`DirectionExtensions` + `WindowNavigationEngine`)
  - MODIFY `MyExtension/CardinalMovment/Direction.cs` (add `static class DirectionExtensions { Axis Axis(this Direction); int Sign(this Direction); }`)
  - MODIFY `MyExtension/CardinalMovment/WindowMatrix.cs` (delete the 5 filter methods :155-409, `AdjacencySize` :141-147 [only called by `SortByLargestAdjacency`], `RemoveHiddenOrTabbedWindows` :411-420, `ReduceWindowsAndSelectActive` :429-456, dead `RemoveWindowsNotAdjacent` :245-283, dead private `ActivateWindow(EnvDTE.Window)` :479-483, ctor-only field `m_IVsFrames` :15 → local; `NavigateInDirection` :463-472 becomes the thin shell below)
  - MODIFY `tests/NeoVisual.Tests/Program.cs` (add `Run_WindowNavigationEngine_*`)
  - MODIFY docs (doc-ref, required — the filter-method names become un-greppable): `.opencode/skills/vs-extension-dev/SKILL.md:108,145-164` and `docs/spec.md:89-104` (the "navigation algorithm" sections list `RemoveHiddenOrTabbedWindows`/`RemoveWindowsInWrongDirection`/`RemoveWindowsNotAligned`/`RemoveWindowsByClosestAdjacency`/`SortByLargestAdjacency`/`ReduceWindowsAndSelectActive` — rewrite to describe the engine's `SelectTarget` pipeline); `docs/progress.md:1207` (M8) and `docs/architecture-review.md:348` reference `RemoveWindowsByClosestAdjacency`/`ReduceWindowsAndSelectActive` — reword so `pwsh tools/check-doc-refs.ps1` passes. **Also `docs/architecture-review.md:286,402`** backtick `RemoveWindowsNotAdjacent` (deleted in N3) — reword both so the lint passes. **Also `ActivateWindow`** (backticked at `docs/architecture-review.md:402`) disappears from source after BP-4 deletes `WindowControlAdapter.cs` (its last `ActivateWindow` at `:169`) and BP-3 deletes `WindowMatrix`'s private `ActivateWindow` — reword it in the same doc-ref pass so the lint passes.
- **Change:** pure, dependency-free seam (the `OverlayKeyHandler` pattern). **The engine reproduces the CURRENT algorithm exactly** (verified against `WindowMatrix.cs:155-456`):
  - `enum Direction { Up, Down, Left, Right }`, `enum Axis { X, Y }` (from BP-1).
  - `static class DirectionExtensions { public static Axis Axis(this Direction d) => (d == Direction.Up || d == Direction.Down) ? Axis.X : Axis.Y; public static int Sign(this Direction d) => (d == Direction.Up || d == Direction.Left) ? -1 : 1; }`.
  - **Exact predicate formulas** (the current `RemoveWindowsInWrongDirection`/`RemoveWindowsNotAligned` bodies, `WindowMatrix.cs:361-409,200-239`):
    - `IsInDirection(c, a, d)`: `Up → c.Y < a.Y`; `Down → c.Y - a.Y > DownTolerancePixels` (**top vs top**, the current `:385` predicate — NOT `GapTo > 1`); `Left → c.X < a.X`; `Right → c.X > a.X`.
    - `IsAligned(c, a, d)`: `Up/Down → a.X <= c.Right && c.X <= a.Right` (X-overlap); `Left/Right → a.Y <= c.Bottom && c.Y <= a.Bottom` (Y-overlap).
  - `sealed class WindowNavigationEngine` with `public static int? SelectTarget(RectCoordinate active, IReadOnlyList<RectCoordinate> candidates, Direction direction, NavigationSettings settings)` — returns the **index** of the winning candidate or `null`. It runs a `private static readonly List<Func<RectCoordinate, RectCoordinate, Direction, bool>> Pipeline` (candidate, active, direction) — `(c,a,d) => !c.IsEmpty` (RemoveHiddenOrTabbedWindows), `(c,a,d) => IsInDirection(c,a,d)` (RemoveWindowsInWrongDirection), `(c,a,d) => IsAligned(c,a,d)` (RemoveWindowsNotAligned) — as **ONE O(n) pass** (two linear scans, no 5 list-mutations + sort; each predicate applied once — M8 deepened):
    1. Scan 1: over candidates passing the whole pipeline, compute `minGap = min(c.GapTo(active, direction))`; if none, return `null`.
    2. Scan 2: among candidates passing the pipeline with `gap in [minGap, minGap + divide]` (`divide = settings.YDivide` for Up/Down, `settings.XDivide` for Left/Right), pick the **max `c.Adjacency(active, direction.Axis())`** — the current algorithm's PRIMARY criterion is largest adjacency (within the divide window of the min gap), NOT min-gap. **Ties broken by LAST-in-list order** (matching the current `SortByLargestAdjacency` = sort-ascending + `Reverse()` + `First()`, which effectively picks the last-in-original-order for equal adjacency).
  - Preserved quirks (behavior-preserving): the DOWN `> 1` pixel tolerance (`WindowMatrix.cs:385`) becomes a named const `private const int DownTolerancePixels = 1` (m21) and is applied in `IsInDirection(Down)` as `c.Y - a.Y > DownTolerancePixels`; the divide window `[min, min+divide]`; the hidden `0,0,0,0` exclusion.
  - `WindowMatrix` becomes a thin COM shell: ctor keeps the enumeration/pairing (via the current trio until BP-4) + `_settings = NavigationSettings.FromSystemDpi()`; `NavigateInDirection(char)` keeps the guard (`m_activeWindow == null || m_ActiveWindows.Count == 0 || m_activeWindow.AutoHides()`), maps `char → Direction` (`U/D/L/R` from `CardinalNavigationConstants`), snapshots `active.coordinates` + `m_ActiveWindows.Select(w => w.coordinates)`, calls `WindowNavigationEngine.SelectTarget`, activates `m_ActiveWindows[target.Value].ActivateWindow()`, and keeps the try/catch + `[NeoVisual]Window navigation failed: ...` `Debug.WriteLine` (`WindowMatrix.cs:453-454`).
- **Verify-with:** NEW tests in `tests/NeoVisual.Tests` (RED: `WindowNavigationEngine` doesn't exist → compile error). All use `NavigationSettings.FromDpi(96,96)` (XDivide=24, YDivide=100) and `active = new RectCoordinate(100,100,100,100)` (active Right=200, Bottom=200). **The tests pin the CURRENT algorithm** (max adjacency within the divide window, last-wins ties, DOWN `c.Y - a.Y > 1`):
  - `Run_WindowNavigationEngine_Up_PicksLargestAdjacency` — candidates `(100,0,100,50)` (gap 50, adjacency 100) and `(150,0,50,50)` (gap 50, adjacency 50) → index 0 (max adjacency 100; both gap 50 in the divide window).
  - `Run_WindowNavigationEngine_Down_ToleranceExcludes` — candidates `(100,101,100,50)` (`c.Y - a.Y = 1`, EXCLUDED by the `>1` tolerance) and `(100,102,100,50)` (`c.Y - a.Y = 2`, passes) → index 1 (pins the m21 quirk: the y=101 candidate is excluded).
  - `Run_WindowNavigationEngine_Left_PicksLargestAdjacency` — candidates `(0,100,50,100)` (gap 50, adjacency 100) and `(0,150,50,50)` (gap 50, adjacency 50) → index 0.
  - `Run_WindowNavigationEngine_Right_PicksLargestAdjacency` — candidates `(250,100,50,100)` (gap 50, adjacency 100) and `(250,150,50,50)` (gap 50, adjacency 50) → index 0.
  - `Run_WindowNavigationEngine_EmptyCandidates_ReturnsNull` — empty list → `null`.
  - `Run_WindowNavigationEngine_NoCandidateInDirection_ReturnsNull` — only a below candidate `(100,201,100,50)`, direction Up → `null`.
  - `Run_WindowNavigationEngine_NotAligned_Excluded` — `(0,0,50,50)` is above but not X-aligned (no overlap with `[100,200)`) → `null`.
  - `Run_WindowNavigationEngine_AdjacencyTie_LastWins` — candidates `(100,0,100,50)` (gap 50, adjacency 100) and `(100,20,100,50)` (gap 30, adjacency 100) — both adjacency 100 (tie) → index 1 (LAST-in-list wins, matching sort+Reverse+First).
  - `Run_WindowNavigationEngine_DivideWindow_ExcludesBeyond` — candidates `(100,0,100,50)` (gap 50, min) and `(100,-200,100,50)` (gap 250 > 50+100) → index 0 (the gap-250 candidate is beyond the divide window).
  - `Run_WindowNavigationEngine_HiddenZeroRect_Excluded` — candidates `(0,0,0,0)` (empty, excluded) + `(100,0,100,50)` → index 1.
  - `dotnet run --project tests/NeoVisual.Tests` green. Diagnostic unchanged: `[NeoVisual] navigate direction={direction}` (`InputHandler.cs:453`, byte-identical).
- **Fails-if:** `Run_WindowNavigationEngine_*` fails; `SelectTarget` picks a different window than the current pipeline for the same layout (behavior drift — the tests above are the pinned contract); the DOWN `>1` tolerance (`c.Y - a.Y > 1`) or the divide window changes; `[NeoVisual] navigate direction=...` changes; doc-ref lint fails on the removed filter-method names.

## BP-4 — N1: WindowAdapter (one frame+DTE+rect type)

- **Files:**
  - CREATE `MyExtension/CardinalMovment/WindowAdapter.cs`
  - DELETE `MyExtension/CardinalMovment/IVsFrameView.cs`, `MyExtension/CardinalMovment/WindowControlAdapter.cs`, `MyExtension/CardinalMovment/IVsUIWindowFrameExtractor.cs`
  - MODIFY `MyExtension/CardinalMovment/WindowMatrix.cs` (ctor + `NavigateInDirection` use `WindowAdapter` instead of the trio: `WindowAdapter.Enumerate(package)` → `WindowAdapter.LinkedTo(activeWindow, adapters)` → `WindowAdapter.FindActive(activeWindow, linked)`; `w.coordinates` → `w.Rect`; `ActivateWindow()` → `Activate()`)
  - MODIFY docs (doc-ref, required — the trio becomes un-greppable): `AGENTS.md:376`, `.opencode/skills/vs-extension-dev/SKILL.md:63-65,96,103`, `docs/spec.md:65-67`, `docs/progress.md:772,1236-1237,1239,1241,1246,1247,1301`, `.opencode/agent/code-review-hub.md:255-257,345-346`, `.opencode/agent/neovim_review_hub.md:128-129,188-189`, `docs/architecture-review.md:275,346,351` — replace `IVsFrameView`/`WindowControlAdapter`/`IVsUIWindowFrameExtractor` (and their `.cs` file paths) with `WindowAdapter` so the doc-ref diff gate passes. (NIT: the bare `IVsFrameView`/`IVsUIWindowFrameExtractor` tokens are skipped by the lint's `^IVs` rule — only the `.cs` file paths and `WindowControlAdapter` are lint-gated; the reword is for hygiene.)
- **Change:** one `sealed class WindowAdapter` (namespace `CardinalNavigation`) consolidating the trio (~422 lines → 1 type). Every VS-API method starts with `ThreadHelper.ThrowIfNotOnUIThread()`:
  - Fields `IVsWindowFrame _frame; EnvDTE.Window _dte; RectCoordinate _rect;`; ctor `WindowAdapter(IVsWindowFrame frame, EnvDTE.Window dte)`.
  - `public RectCoordinate Rect => RefreshRect();` — lazy `GetWindowScreenRect` on access (m17/m23 fix: one cached rect, refreshed on read; the engine snapshots rects once per navigation).
  - `public EnvDTE.Window DteWindow => _dte;`
  - `public void Activate() => _dte.Activate();` (from `WindowControlAdapter.ActivateWindow` :169-173).
  - `public bool AutoHides() => _dte.AutoHides;` (from :179-183).
  - `public bool IsOnScreen()` — `_frame.IsOnScreen(out int pf); return pf != 0;` (carried over from the dead `IVsFrameView.IsTabbedAndInvisible` :43; currently uncalled, kept for API completeness).
  - `public static List<WindowAdapter> Enumerate(AsyncPackage package)` — `IVsUIShell.GetToolWindowEnum` + `GetDocumentWindowEnum` → adapters (from `IVsUIWindowFrameExtractor.GetIVsWindowFramesEnumerator` :45-62, pairing via `VsShellUtilities.GetWindowObject`).
  - `public static WindowAdapter? FindActive(EnvDTE.Window activeWindow, IEnumerable<WindowAdapter> windows)` — `FirstOrDefault(a => UtilityMethods.CompareWindows(activeWindow, a.DteWindow))` (m18 fix: `FirstOrDefault` instead of `.First()` + catch).
  - `public static IEnumerable<WindowAdapter> LinkedTo(EnvDTE.Window activeWindow, IEnumerable<WindowAdapter> windows)` — `UtilityMethods.GetLinkedWindowsList(activeWindow.LinkedWindowFrame, windows.Select(w => w.DteWindow).ToList())` then filter by `CompareWindows` (from `GetLinkedWindowControlAdapters` :102-135; the parent-window list is derived from the adapters' `DteWindow`s instead of a separately-enumerated DTE list — equivalent).
  - The `IVsWindowFrameNotify` `NotImplementedException` landmine (`IVsFrameView.cs:129-147`) dies with `IVsFrameView`.
- **Verify-with:** `dotnet build` succeeds; `dotnet run --project tests/Telescope.Tests` (72, after Lanes 1-3) and `tests/NeoVisual.Tests` (58) stay green. No new unit tests (VS-coupled). Diagnostic unchanged: `[NeoVisual] navigate direction={direction}`. `pwsh tools/check-doc-refs.ps1` exits 0 (after the doc-ref update).
- **Fails-if:** compile error (a `WindowControlAdapter`/`IVsFrameView`/`IVsUIWindowFrameExtractor` reference remains anywhere); `WindowAdapter.Enumerate` returns a different frame set than the trio (behavior drift); `Rect` returns a stale/different rect than the old `coordinates`; doc-ref lint fails on the removed symbols.

## BP-5 — Matrix-rebuild cache (perf, behavior-preserving)

- **Files:**
  - MODIFY `MyExtension/WindowManager.cs` (add `using CardinalNavigation;` + `using Microsoft.VisualStudio.Shell;`; add the cached `List<WindowAdapter>` + `GetWindowAdapters(AsyncPackage)`; invalidate in `OnWindowFocusChanged` :110-137)
  - MODIFY `MyExtension/CardinalMovment/WindowMatrix.cs` (ctor `WindowMatrix(AsyncPackage, IVsWindowFrame?)` → `WindowMatrix(List<WindowAdapter> adapters, AsyncPackage package, IVsWindowFrame? currentFrame)` — no enumeration; `package` is kept only for the null-`currentFrame` DTE active-window fallback)
  - MODIFY `MyExtension/InputHandler.cs` (`Navigate` :457 → `new WindowMatrix(_windowManager.GetWindowAdapters(_package), _package, _windowManager.CurrentWindow)`)
- **Change:** `WindowManager` caches the `IVsUIShell` frame enumeration across keystrokes:
  - Fields `private List<WindowAdapter>? _cachedAdapters; private bool _adaptersDirty = true;`.
  - `OnWindowFocusChanged()` sets `_adaptersDirty = true;` (conservative invalidation on the `SEID_WindowFrame` selection event the manager already tracks).
  - `public List<WindowAdapter> GetWindowAdapters(AsyncPackage package) { ThreadHelper.ThrowIfNotOnUIThread(); if (_cachedAdapters == null || _adaptersDirty) { _cachedAdapters = WindowAdapter.Enumerate(package); _adaptersDirty = false; } return _cachedAdapters; }`.
  - `WindowMatrix` ctor takes the cached list; rects are snapshotted lazily per navigation (active + candidates only) via `adapter.Rect` inside `NavigateInDirection` (the engine's `SelectTarget` call) — no per-keystroke enumerate + wrap + O(n²) pairing.
  - **Documented behavior-preserving tradeoff:** the frame enumeration is cached and invalidated on focus-change events; rects are always refreshed per navigation. A window opened/closed without a focus change appears on the next focus change — this is the plan's "at minimum reuse the `IVsUIShell` enumeration across keystrokes".
- **Verify-with:** `dotnet build` succeeds; `dotnet run --project tests/Telescope.Tests` (72, after Lanes 1-3) and `tests/NeoVisual.Tests` (58) stay green. No new unit tests (perf, behavior-preserving). Diagnostic unchanged: `[NeoVisual] navigate direction={direction}` (`InputHandler.cs:453`, byte-identical).
- **Fails-if:** navigation picks a stale rect (a moved window navigates to its old position); a newly opened window never appears in navigation; `WindowManager.GetWindowAdapters` throws on the UI thread; `[NeoVisual] navigate direction=...` changes.

---

## Unit test plan (RED proof)

All new seam tests follow the `OverlayKeyHandler`/`TextMotionNavigator` pattern: dependency-free classes the UI delegates to. **RED proof** = the test references an API that does not exist yet (compile error); GREEN after the merge. All live in `tests/NeoVisual.Tests/Program.cs` (the `Tests` class; `using CardinalNavigation;` already present at :6).

| Test | Merge | RED proof | GREEN assertion |
|------|-------|-----------|-----------------|
| `Run_RectCoordinate_StoresFields` (**UPDATE** :489-496) | N2 | `r.X` doesn't compile (fields are lowercase `x,y,width,height`) | `new RectCoordinate(1,2,3,4)` → `X==1, Y==2, Width==3, Height==4` |
| `Run_RectCoordinate_Right_Bottom` (**NEW**) | N2 | `Right`/`Bottom` don't exist | `(1,2,3,4).Right == 4`, `.Bottom == 6` |
| `Run_RectCoordinate_IsEmpty` (**NEW**) | N2 | `IsEmpty` doesn't exist | `(0,0,0,0).IsEmpty == true`; `(1,0,0,0).IsEmpty == false` |
| `Run_RectCoordinate_Adjacency` (**NEW**) | N2 | `Adjacency`/`Axis` don't exist | X-overlap 5, no-overlap 0, Y-overlap 5 |
| `Run_RectCoordinate_GapTo` (**NEW**) | N2 | `GapTo`/`Direction` don't exist | Up 50, Down 2, Left 50, Right 50 |
| `Run_NavigationSettings_FromDpi` (**NEW**) | N4 | `NavigationSettings` doesn't exist | `FromDpi(96,96)` → 24/100; `(144,144)` → 36/150; `(120,120)` → 30/125 |
| `Run_WindowNavigationEngine_Up_PicksLargestAdjacency` (**NEW**) | N3 | `WindowNavigationEngine` doesn't exist | picks the max-adjacency candidate above (adjacency 100 vs 50, both gap 50) |
| `Run_WindowNavigationEngine_Down_ToleranceExcludes` (**NEW**) | N3 | same | `c.Y - a.Y = 1` excluded (`>1` tolerance), y=102 picked (pins the m21 quirk) |
| `Run_WindowNavigationEngine_Left_PicksLargestAdjacency` (**NEW**) | N3 | same | picks the max-adjacency candidate left (adjacency 100 vs 50, both gap 50) |
| `Run_WindowNavigationEngine_Right_PicksLargestAdjacency` (**NEW**) | N3 | same | picks the max-adjacency candidate right (adjacency 100 vs 50, both gap 50) |
| `Run_WindowNavigationEngine_EmptyCandidates_ReturnsNull` (**NEW**) | N3 | same | empty → `null` |
| `Run_WindowNavigationEngine_NoCandidateInDirection_ReturnsNull` (**NEW**) | N3 | same | no in-direction candidate → `null` |
| `Run_WindowNavigationEngine_NotAligned_Excluded` (**NEW**) | N3 | same | in-direction but not axis-aligned → `null` |
| `Run_WindowNavigationEngine_AdjacencyTie_LastWins` (**NEW**) | N3 | same | equal adjacency → LAST-in-list wins (matches sort+Reverse+First) |
| `Run_WindowNavigationEngine_DivideWindow_ExcludesBeyond` (**NEW**) | N3 | same | beyond `min+divide` excluded |
| `Run_WindowNavigationEngine_HiddenZeroRect_Excluded` (**NEW**) | N3 | same | `0,0,0,0` excluded |

## Verification Trace

| failing test / scenario | implicated steps | expected diagnostic / pass signal |
|---|---|---|
| `Run_RectCoordinate_StoresFields` (updated) + `Run_RectCoordinate_Right_Bottom` / `IsEmpty` / `Adjacency` / `GapTo` (RED: members don't exist) | BP-1 | `[NeoVisual] navigate direction={direction}` unchanged; `Right`/`Bottom`/`IsEmpty`/`Adjacency`/`GapTo` return the current inline-math values |
| `Run_NavigationSettings_FromDpi` (RED: class doesn't exist) | BP-2 | `FromDpi` divide math equals `SetWindowDivideSelectionSizes` for real DPI values |
| `Run_WindowNavigationEngine_*` (RED: class doesn't exist) | BP-3 | `[NeoVisual] navigate direction={direction}` unchanged; `SelectTarget` reproduces the 6-step pipeline selection for every layout |
| `tests/NeoVisual.Tests` (58) | BP-1..BP-5 | all existing `Run_*` pass |
| `tests/Telescope.Tests` (72) | BP-1..BP-5 | all existing `Run_*` pass (no Telescope file touched; gate is both suites) |
| `pwsh tools/check-doc-refs.ps1` | BP-2, BP-3, BP-4 | exit 0 (doc-ref lint) |
| A3 log-literal check (`git diff`) | BP-1..BP-5 | no `[Telescope]`/`[NeoVisual]`/`[Hook]` **structured-log** literal changed |
| e2e gate (deferred — informational, queued in `e2e-queue.md`, NOT executed) | BP-1..BP-5 | `neovisual-window-nav` + `neovisual-toolwindow` stay green on a capable machine; `[NeoVisual] navigate direction=...` unchanged |

**Known-RED allowlist (do NOT report as regressions):** none — `docs/progress.md` reports no known-RED e2e scenario remains (all 35 green as of 2026-09-27); e2e is deferred to `e2e-queue.md` anyway.

## KEY DECISIONS (build-agent must not second-guess)

- **`Axis`/`Direction` enums land in BP-1 (N2), not BP-3 (N3).** `RectCoordinate.Adjacency(RectCoordinate, Axis)` and `GapTo(RectCoordinate, Direction)` reference them, so they must exist before the struct compiles. BP-3 only adds the `Axis()/Sign()` extensions + the engine.
- **`NavigationSettings.FromDpi(int,int)` is added alongside `FromSystemDpi()`** so the divide math is hermetically unit-testable without `DpiAwareness` (the `OverlayKeyHandler` pattern). `FromSystemDpi()` is the only VS-coupled entry point.
- **`CardinalNavigationConstants` keeps the 3 live divide constants** (`DefaultLogicalXWindowDivide`, `DefaultLogicalTabPaneDivide`, `DefaultLogicalSelectorScale`) — only the 4 dead constants are deleted. This keeps `SKILL.md:112` doc-ref resolvable and matches the plan's "delete the dead constants".
- **The engine's "ONE O(n) pass" is two linear scans** (min-gap, then winner-selection) — no list mutations, no sort. A literal single scan is impossible because the divide window `[min, min+divide]` depends on the global min. This satisfies the plan's intent (O(n), no 5 list-mutations + sort, M8 deepened).
- **Preserved quirks:** the DOWN `>1` pixel tolerance (named `DownTolerancePixels = 1`, m21), the divide window `[min, min+divide]`, the hidden `0,0,0,0` exclusion, and the `[NeoVisual] navigate direction={direction}` diagnostic byte-identically. `CheckDte` (`WindowMatrix.cs:118`) is intentionally left (not in the Lane 4 merge scope per the plan table).
- **`WindowMatrix` ctor keeps the `AsyncPackage` parameter after BP-5** (as `WindowMatrix(List<WindowAdapter>, AsyncPackage, IVsWindowFrame?)`) solely for the null-`currentFrame` DTE active-window fallback — the enumeration itself comes from the cached list.


---

# Lane 5 — Cross-cutting (rev1, source-verified)

> **Lane 5 of the architecture-consolidation program** (`docs/architecture-consolidation.md`,
> 2026-09-28), executed top-to-bottom by the build-agent in the lane order
> **X1 → X2 → C8 → X4** (BP-1..BP-4, one BP step per merge). Every merge is
> **behavior-preserving**; no new behavior.
>
> **Gate:** both offline unit suites stay green (`dotnet run --project tests/Telescope.Tests`
> → 72, growing to 77 with the new X1 tests; `tests/NeoVisual.Tests` → 73 — after Lanes 1-4); the new X1 seam tests
> are **RED before the merge and GREEN after**; `pwsh tools/check-doc-refs.ps1` exits 0 after the
> X4 doc step; `git diff` shows no change to any `[Telescope]`/`[NeoVisual]`/`[Hook]`
> **structured-log** literal (M-M7 NOT triggered). e2e is deferred to `e2e-queue.md` — **no e2e
> scenario name appears in any Verify-with**; the e2e gate column in the merges table is
> informational only.
>
> **Known-RED allowlist:** none — `docs/progress.md` reports no known-RED e2e scenario remains
> (all 35 green as of 2026-09-27); e2e is deferred anyway. Two **documented deviations**
> (adjudicated, not regressions) are carried in the Verification Trace: BP-1 changes the hook's
> install/uninstall lines from the `[GlobalKeyboard]` prefix to `[Hook]` (the "one `[Hook]`
> prefix" target), and BP-1 routes error-path diagnostics through the facade (see BP-1 + KEY
> DECISIONS).
>
> **Hard requirements honored throughout:** net472 (no `IReadOnlySet<T>` — use
> `IReadOnlyCollection<Keys>`/`IReadOnlyDictionary`), `ThreadHelper.ThrowIfNotOnUIThread()` on
> every new VS-API method, LangVersion 14, Nullable enabled. No `[Telescope]`/`[NeoVisual]`/
> `[Hook]` log literal changes anywhere.

---

## 1. Merges table (current state verified against source, 2026-09-28)

| id | Merge | Current state (verified file:line) | Target design (exact API) | Blast radius | Coverage (unit tests) | e2e gate (deferred, informational) |
|----|-------|-----------------------------------|---------------------------|--------------|-----------------------|-------------------------------------|
| X1 | **LogPath** | log trio well-factored: `NeoVisualLog.Log` `NeoVisualLog.cs:90-99` (`LogFileWriter.Write` + `Debug.WriteLine` + `WriteToPane`); `LogFileWriter.Write` `:54-57` / `WriteDebug` `:60-63` → `Append` `:82-102` does `File.AppendAllText` per call (`:95`, m45 typing hot path); `NeoVisualTraceListener` `:18-32` bridges Debug→`WriteDebug`; **52 verified `Debug.WriteLine` call sites bypass the facade across 17 files** (60 raw grep matches across 19 files incl. 8 doc-comment refs + the facade's own internal call `NeoVisualLog.cs:97`); **6 of the 52 die in Lane 1 BP-3** (5 in `InputHandler` + 1 in `MyExtensionPackage` — annotated as C2 notes below), so the conversion list enumerates the **46 surviving sites** (15 Telescope + 2 FinderBase + 28 MyExtension + 1 TelescopeLauncher); hook uses two prefixes: `[Hook]` per-key `GlobalKeyboardHook.cs:124` vs `[GlobalKeyboard]` in the `Log` helper `:230` (which emits the install/uninstall messages at `:72,:77,:244`) | `public static void NeoVisualLog.Debug(string message)` (documented alias for `Log` — routes through the full facade pipeline: structured file + debug output + pane; text byte-identical); `LogFileWriter` buffered (kept-open `StreamWriter` per file, `FileMode.Append`/`FileAccess.Write`/`FileShare.Read`, UTF-8 no BOM, flush on ~200ms timer + explicit `Flush()` + `Close()`; `Clear()` flushes+closes+truncates); `public static void NeoVisualLog.Close()`; hook `Log` helper `:228-233` → `[Hook]` prefix + `NeoVisualLog.Debug` | whole repo (all log call sites + the log write path) | **NEW** `Run_LogFileWriter_Buffered_*` (RED: buffering doesn't exist); **UPDATE** `Run_LogFileWriter_WritesAndClearsFile` (`tests/Telescope.Tests/Program.cs:114-146`) | full suite (log lines are the contract) |
| X2 | **HarnessHelpers** | helpers triplicated: `test-e2e.ps1` `:80-83` (Write-*), `:191-196` (Send-Text — F41 char-code-as-VK), `:203-208` (Bring-ToForeground), `:440-456` (Wait-NewLogLine — F39 O(n²) whole-file re-read), `:458-475` (Wait-NewLogLineAfter), `:477-488` (Wait-LogContains), `:496-512` (Assert-NoEnterStorm), `:1909-1925` (KbInject + Win32.Fg Add-Type), `:1992-1998` (VS-root resolution); `iterate-telescope.ps1` `:6` (stale doc comment — F37), `:60-63` (Write-*), `:70-85` (VS-root), `:91-107` (KbInject + Win32.Fg), `:109-119` (Wait-LogContains), `:232-238` (Bring-ToForeground), `:274-277` (Send-Text char-code), `:318-321` (divergent overlay-close — F37); `dte-command.ps1` `:13-18` (hardcoded VS paths — F44); runner `test-e2e.ps1:2058-2069` ignores `$false` returns (F38) + failure output lacks log tail/step context (F40) | `tools/harness-common.ps1` (shared module: prefix vars, Write-*, KbInject, Win32.Fg, Wait-LogContains, Wait-NewLogLine, Wait-NewLogLineAfter, Bring-ToForeground, Send-Tap/Send-Shift/Send-Text/Send-Ctrl, Reset-LogBaseline, Assert-NewLogLine, Assert-NoEnterStorm, Close-Telescope, Resolve-VsRoot); align iterate-telescope.ps1's close with `Close-Telescope`; fix F37/F38/F39/F40/F41/F44 | tools/ only | none (harness) | e2e suite |
| C8 | **HookInterest** | `IsInteresting` `GlobalKeyboardHook.cs:158-190` hardcodes H/J/K/L/I/Space/Escape/Ctrl in the switch `:174-189` — the default controller's key set duplicated in the hook; `InputHandler.cs:310-312` hardcodes hjkl + `controller.ActionKeys.Contains` | `public bool InputHandler.IsKeyOfInterest(Keys key, bool ctrl, bool shift, bool alt)` = leader-active/modifier-chord OR leader-key OR Escape OR Ctrl-swallow OR (tool-window normal mode AND key ∈ `DefaultControllerKeys` ∪ `controller.ActionKeys`); `GlobalKeyboardHook.IsInteresting` → `_inputHandler.IsKeyOfInterest(key, ctrl, shift, alt)`; delete the switch `:174-189` | hook + InputHandler | none (VS-coupled; optional pure key-set extraction) | neovisual-toolwindow/textinput e2e |
| X4 | **ProjectLayering** | host (`MyExtension`) depends on `Telescope` for core infra: `MyExtension/MyExtension.csproj:32` (`ProjectReference`), 92 `Telescope.NeoVisualLog`/`Telescope.TelescopeController`/`Telescope.DiagnosticLog` usages; `Telescope.csproj:11` (VS SDK) + `:15-21` (WPF refs) — Telescope is itself VS-coupled; only `OverlayKeyHandler`/`TextMotionNavigator`/`FzfFilter`/`LogFileWriter` are VS-free | **DECISION, not a code change:** (a) accept + document the seam explicitly (F46's recommendation — the roadmap must not grow more VS-coupled code inside the "library"); document in `docs/architecture-consolidation.md` (X4 section `:137`) + `docs/spec.md` (durable, linted) | project structure / docs only | none | build + unit suites |

**Lane 5 acceptance:** both unit suites stay green; `git diff` shows no change to any
`[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal; the new X1 buffered-log tests are
RED before the merge and GREEN after; `pwsh tools/check-doc-refs.ps1` exits 0 after the X4 doc
step.

---

## 2. Build Plan — Lane 5 (BP-1..BP-4)

## BP-1 — X1: LogPath (facade Debug + buffered LogFileWriter + one [Hook] prefix)

- **Files:**
  - MODIFY `Telescope/NeoVisualLog.cs` (add `public static void Debug(string message)`; add `public static void Close()`)
  - MODIFY `Telescope/LogFileWriter.cs` (buffered writes: kept-open `StreamWriter` per file, `Flush()`/`Close()`, ~200ms timer, path-change reopen, `Clear()` flush+close+truncate)
  - MODIFY `MyExtension/GlobalKeyboardHook.cs` (`Log` helper `:228-233`: prefix `[GlobalKeyboard]`→`[Hook]`, body → `NeoVisualLog.Debug(fullMessage)`)
  - MODIFY the **17 files with the 52 `Debug.WriteLine` sites** → `NeoVisualLog.Debug(...)` (all `file:line` are baseline-relative — re-locate by symbol after the earlier lanes; see the header convention):
    - Telescope (7 files, 15 sites): `Telescope/CodeIssuesFinder.cs` (`:80,117,135,200`), `Telescope/FileFinder.cs` (`:80,112`), `Telescope/GrepFinder.cs` (`:93,131,149`), `Telescope/ImplementationFinder.cs` (`:57,88`), `Telescope/ReferencesFinder.cs` (`:56,89`), `Telescope/TelescopeController.cs` (`:56`), `Telescope/TelescopeOverlay.cs` (`:951`)
    - **`Telescope/FinderBase.cs` (2 sites — created in Lane 3 BP-2):** the `GatherErrorLiteral` call in `GetCandidates` and the `FinderNameForErrors` line in `OnSelected` (**C4:** without this the X1 grep gate "exactly 1 match" fails — the count would be 3).
    - MyExtension (9 files, 35 sites): `MyExtension/GlobalKeyboardHook.cs` (`:231`), `MyExtension/InputHandler.cs` (`:131,152,226,592` — **C2:** the 5 sites `:477,498,520,542,563` were inside the open-finder methods deleted in Lane 1 BP-3; they now live in `TelescopeLauncher.cs`), `MyExtension/KeybindingConfig.cs` (`:87,101,104`), `MyExtension/CardinalMovment/UtilityMethods.cs` (`:71`), `MyExtension/MyExtensionPackage.cs` (`:50,102,725` — **C2:** `:422` was inside `OpenTelescope`, deleted in Lane 1 BP-3), `MyExtension/CardinalMovment/WindowMatrix.cs` (`:89,127,453`), `MyExtension/VimModeTracker.cs` (`:255,265,285,332,392,421,441,462,503,507`), `MyExtension/ToolWindows/SolutionExplorerController.cs` (`:161,370,478`)
    - **`MyExtension/TelescopeLauncher.cs` (1 site — created in Lane 1 BP-3):** the parameterized `Failed to open Telescope {finderName}` `Debug.WriteLine` (**C2:** the 5 collapsed error-path sites moved here).
    - **C1:** `MyExtension/CardinalMovment/WindowControlAdapter.cs` (`:85,131`) is NOT in this list — the file is DELETED in Lane 4 BP-4 (N1); its 2 `Debug.WriteLine` sites die with it (the `WindowAdapter` replacement carries no `Debug.WriteLine`). Nothing to convert here.
  - MODIFY `MyExtension/MyExtensionPackage.cs` (`Dispose(bool)` `:777-789` → add `Telescope.NeoVisualLog.Close()`)
  - MODIFY `tests/Telescope.Tests/Program.cs` (update `Run_LogFileWriter_WritesAndClearsFile` `:114-146`; add `Run_LogFileWriter_Buffered_*`)
- **Change:**
  - `public static void NeoVisualLog.Debug(string message) => Log(message);` — a **documented alias for `Log`** (the `Log` body at `NeoVisualLog.cs:90-99` already does `LogFileWriter.Write` + `Debug.WriteLine` + `WriteToPane`). **Do NOT write `Debug.WriteLine(msg) + NeoVisualLog.Log(msg)` literally** — `Log` already calls `Debug.WriteLine` internally (`NeoVisualLog.cs:97`), so that would double-write every message to the debug-output file. `Debug` routes error-path diagnostics through the full facade pipeline (structured exp log + pane + debug file); the text of each line is byte-identical — only the write path changes.
  - `LogFileWriter` becomes buffered: a kept-open `StreamWriter` per file, lazily opened on first write as `new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))`. **`FileShare.Read` is mandatory** — the e2e harness `Get-Content`s the log file while VS holds it open (the current `File.AppendAllText` at `:95` also uses `FileShare.Read`); **UTF-8 without BOM** matches `File.AppendAllText`'s encoding so the file bytes are identical. The writer is closed + reopened when `LogPath`/`DebugLogPath` changes (the unit tests repoint them). Line format unchanged: `string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss.fff} {1}", DateTime.Now, message)` + `Environment.NewLine` — **the timestamp is captured at `Write()` time, NOT `Flush()`** (rev3: otherwise all buffered lines share one timestamp and `Run_LogFileWriter_Buffered_ContentIdenticalToAppend` fails). New members: `internal static void Flush()` (flushes both writers), `internal static void Close()` (flush + dispose writers + dispose the timer), a `System.Threading.Timer` (~200ms) that flushes both writers, and `Clear()` (`:36-51`) becomes flush → close writers → truncate both files (preserving the once-per-process `_clearedThisProcess` flag). All under the existing `Sync` lock. `NeoVisualLog.Close()` delegates to `LogFileWriter.Close()` and is called from `MyExtensionPackage.Dispose(bool)` (`:777-789`).
  - `GlobalKeyboardHook.Log` (`:228-233`): `string fullMessage = $"{Telescope.DiagnosticLog.Hook}{DateTime.Now:HH:mm:ss.fff}  {message}"; NeoVisualLog.Debug(fullMessage);` — one `[Hook]` prefix for all hook diagnostics (the per-key line at `:124` already uses `[Hook]`). **Documented deviation:** the install/uninstall lines change prefix `[GlobalKeyboard]` → `[Hook]`; `DiagnosticLog.GlobalKeyboard` (`DiagnosticLog.cs:15`) is **KEPT** (still pinned by `Run_LogPrefixes_Pinned` at `tests/Telescope.Tests/Program.cs:946`) but is no longer emitted by the hook. The harness never asserts on `[GlobalKeyboard]` (grep-verified: no `[GlobalKeyboard]` in `tools/`).
- **Verify-with:** NEW tests in `tests/Telescope.Tests` (RED: buffering doesn't exist — `Flush()`/`Close()` don't compile; `Run_LogFileWriter_Buffered_NotFlushedYet` fails because the current code writes immediately): `Run_LogFileWriter_Buffered_NotFlushedYet` (`Write("x")`, assert `File.ReadAllText(logPath)` does NOT contain "x"), `Run_LogFileWriter_Buffered_FlushWritesToDisk` (`Write("x")`, `Flush()`, assert file contains "x"), `Run_LogFileWriter_Buffered_ContentIdenticalToAppend` (write 3 lines, `Flush()`, assert the file content matches the per-call-append format — timestamped lines, byte-identical), `Run_LogFileWriter_Buffered_FlushOnClose` (`Write("x")`, `Close()`, assert file contains "x"), `Run_LogFileWriter_Buffered_PathChangeReopens` (write to path A, `Flush()`, repoint `LogPath` to path B, write, `Flush()`, assert A has only line 1 and B has only line 2). UPDATE `Run_LogFileWriter_WritesAndClearsFile` (`:114-146`): add `LogFileWriter.Flush()` after the `Write`/`WriteDebug` calls and before the `File.ReadAllText` assertions (A4 — the buffered write is not on disk until flushed); the `Clear()` + empty-file assertions stay. **Also UPDATE `Run_TelescopeLog_Prefix` + `Run_TelescopeLog_EmptyMessage` (added in Lane 1 BP-6):** they do `TelescopeLog.Log(...)` then immediately `File.ReadAllText`-assert — add `LogFileWriter.Flush()` after the `TelescopeLog.Log(...)` call and before the assertions (F3 — under buffering they fail without the flush). `dotnet build` succeeds; `dotnet run --project tests/Telescope.Tests` (56→77 after all lanes) and `tests/NeoVisual.Tests` (38→73) stay green; `git diff` shows no `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal change; grep `Debug.WriteLine` in `MyExtension/` + `Telescope/` → exactly 1 match (`NeoVisualLog.cs:97`, the facade's own internal call).
- **Fails-if:** `Run_LogFileWriter_Buffered_*` fails; `Run_LogFileWriter_WritesAndClearsFile` fails after the update; a `[Telescope]`/`[NeoVisual]`/`[Hook]` literal changes (`git diff`); the harness can no longer read the log file while VS holds it open (FileShare violation — the writer must use `FileShare.Read`); the log file content differs from the per-call append (encoding/timestamp drift); a `Debug.WriteLine` site is missed (grep count > 1 in `MyExtension/` + `Telescope/`).

## BP-2 — X2: HarnessHelpers (tools/harness-common.ps1)

- **Files:**
  - CREATE `tools/harness-common.ps1`
  - MODIFY `tools/test-e2e.ps1` (dot-source `harness-common.ps1`; delete the moved helpers; fix the runner F38/F40; fix `Send-Text` F41; fix `Wait-NewLogLine`/`Wait-NewLogLineAfter`/`Wait-LogContains`/`Assert-NoEnterStorm` F39)
  - MODIFY `tools/iterate-telescope.ps1` (dot-source `harness-common.ps1`; delete the moved helpers; align the overlay-close `:318-321` with `Close-Telescope` F37; fix the stale doc comment `:6` F37; fix `Send-Text` F41)
  - MODIFY `tools/dte-command.ps1` (use the shared `Resolve-VsRoot` F44)
- **Change:** one shared `tools/harness-common.ps1` (dot-sourced by all three scripts) holding the duplicated helper set: the `$script:Pfx*` prefix vars (`test-e2e.ps1:175-178` / `iterate-telescope.ps1:27-30`), `Write-Step`/`Write-Info`/`Write-Pass`/`Write-Fail` (`test-e2e.ps1:80-83` / `iterate-telescope.ps1:60-63`), the `KbInject` + `Win32.Fg` Add-Type blocks (`test-e2e.ps1:1909-1925` / `iterate-telescope.ps1:91-107`), `Wait-LogContains` (`test-e2e.ps1:477-488` / `iterate-telescope.ps1:109-119`), `Wait-NewLogLine` (`test-e2e.ps1:440-456`), `Wait-NewLogLineAfter` (`:458-475`), `Bring-ToForeground` (`test-e2e.ps1:203-208` / `iterate-telescope.ps1:232-238`), `Send-Tap`/`Send-Shift`/`Send-Text`/`Send-Ctrl` (`test-e2e.ps1:184-202`), `Reset-LogBaseline` (`:180-182`), `Assert-NewLogLine` (`:490-494`), `Assert-NoEnterStorm` (`:496-512`), `Close-Telescope` (`:427-438`), and `Resolve-VsRoot` (the VS-root resolution triplicated at `test-e2e.ps1:1992-1998`, `iterate-telescope.ps1:70-85`, `dte-command.ps1:13-18`). **Fixes folded in:** F37 — iterate-telescope.ps1's close (`:318-321`) becomes the shared `Close-Telescope $vsProc $logPath` (no `Bring-ToForeground` before the Escapes — that steals focus from the modal; Escape pairs until `overlay closed`), and the stale doc comment `:6` (`%APPDATA%\MyExtension\neovisual.log`) is corrected to the real per-run path `<solution>\log\<index>-neovisual-exp.log`; F38 — the runner (`test-e2e.ps1:2062-2068`) captures the scriptblock's return value and treats `$false` as a failure (`$result = & $script:Scenarios[$name] $vsProc $logPath; $ok = ($result -ne $false)`); F39 — `Wait-NewLogLine`/`Wait-NewLogLineAfter`/`Wait-LogContains`/`Assert-NoEnterStorm` read only the appended tail (O(n) total, not O(n²) whole-file re-reads). **CRITICAL CONSTRAINT (rev3):** the optimization MUST keep the fixed log baseline — always search from `$script:LogBaseline`, NEVER advance a cursor on match (AGENTS.md hard-requires "Do NOT reintroduce a cursor that advances on match"). Only the FILE READ is optimized (e.g. cache the last-known EOF offset and re-scan from the baseline), never the search window. **e2e-only risk:** the baseline semantics are not verifiable by the unit gate (e2e deferred) — the deferred e2e suite must re-verify no stale line satisfies a later assertion; F40 — the runner's catch (`:2065-2067`) appends the log tail + the failing step to the failure record; F41 — `Send-Text` maps punctuation to the correct VKs (a char→VK map for `'`/`!`/`?`/`.`/`,`/`-`/`_`/space etc. — the current `[int][char]::ToUpper($ch)` only works for A-Z); F44 — `dte-command.ps1` resolves the VS `PublicAssemblies` dir via the shared `Resolve-VsRoot` instead of the hardcoded `:13-16` paths.
- **Verify-with:** `pwsh -NoProfile -File tools/test-e2e.ps1 -List` exits 0 and lists all 35 scenarios (proves test-e2e.ps1 parses + registers after the refactor; the `-List` branch exits before any VS boot); parse-check the other two scripts: `pwsh -NoProfile -Command "$t=$null;$e=$null;[System.Management.Automation.Language.Parser]::ParseFile('tools/iterate-telescope.ps1',[ref]$t,[ref]$e)|Out-Null;if($e.Count){$e|%{$_.Message};exit 1}else{'iterate parse OK'}"` and the same for `tools/dte-command.ps1`; dot-source check: `pwsh -NoProfile -Command "& { . ./tools/harness-common.ps1; if (-not (Get-Command Close-Telescope -ErrorAction SilentlyContinue)) { throw 'Close-Telescope missing' }; 'harness-common OK' }"`. No new unit tests (harness).
- **Fails-if:** `tools/test-e2e.ps1 -List` fails to parse or lists fewer than 35 scenarios (a helper was moved but not dot-sourced); a shared function is missing from `harness-common.ps1` (dot-source check throws); `iterate-telescope.ps1`/`dte-command.ps1` fail to parse; the runner still ignores `$false` returns (F38 not fixed); `Send-Text` still maps punctuation to wrong VKs (F41 not fixed).

## BP-3 — C8: HookInterest (IsKeyOfInterest delegation)

- **Files:**
  - MODIFY `MyExtension/InputHandler.cs` (add `public bool IsKeyOfInterest(Keys key, bool ctrl, bool shift, bool alt)` + `internal static readonly IReadOnlyCollection<Keys> DefaultControllerKeys`)
  - MODIFY `MyExtension/GlobalKeyboardHook.cs` (`IsInteresting` `:158-190` → `_inputHandler.IsKeyOfInterest(key, ctrl, shift, alt)`; delete the switch `:174-189`)
- **Change:** `internal static readonly IReadOnlyCollection<Keys> DefaultControllerKeys` = `{ Keys.H, Keys.J, Keys.K, Keys.L, Keys.I }` (hjkl + i — the default controller's key set, currently duplicated in the hook switch `:174-189` and in `InputHandler.HandleKey` `:310-312`). `public bool IsKeyOfInterest(Keys key, bool ctrl, bool shift, bool alt)`:
  1. `if (IsLeaderActive || ctrl || shift || alt) return true;` — **the modifier-chord check MUST be preserved** (the task's formula omits it, but without it Ctrl+H/Ctrl+W simple shortcuts never reach `HandleKey` — a regression).
  2. `if (key == _leaderKey) return true;` — the leader key (Space by default) starts a sequence.
  3. `if (key == Keys.Escape) return true;` — cancels a leader sequence / exits tool-window input mode.
  4. `if (key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey) return true;` — Ctrl swallow while a completion popup is open.
  5. `if (FocusGuard.ShouldRouteToolWindowKey(_windowManager.IsToolWindow, _vsVim.IsEditorFocused, _windowManager.CurrentController?.IsInputMode == true, GeneralToolWindowController.IsTextInputType(_windowManager.Type))) { var c = _windowManager.CurrentController; if (c != null && !c.IsInputMode && (DefaultControllerKeys.Contains(key) || c.ActionKeys.Contains(key))) return true; }` — tool-window normal mode. (**M3/rev3:** this is the **4-arg** form from Lane 2 BP-4 (T5) — the 2-arg `ShouldRouteToolWindowKey(isToolWindow, EditorFocusedVeto)` no longer exists after T5; `EditorFocusedVeto` is retained for the `IsTyping` call only.)
  6. `return false;`
  `GlobalKeyboardHook.IsInteresting` (`:158-190`) becomes `private bool IsInteresting(Keys key, bool ctrl, bool shift, bool alt) => _inputHandler.IsKeyOfInterest(key, ctrl, shift, alt);` — the switch is deleted. The pre-filter stays cheap (all reads are in-process properties; no COM/interop, no per-key logging added — F2 not reintroduced). Optional (not required): extract `DefaultControllerKeys` into a pure static helper for a trivial unit test.
- **Verify-with:** `dotnet build` succeeds; `dotnet run --project tests/Telescope.Tests` (77, after all lanes) and `tests/NeoVisual.Tests` (73) stay green. Diagnostics unchanged: `[NeoVisual] toolwindow-move key=... -> arrow vk=...`, `[NeoVisual] leader-binding executed: ...`, `[NeoVisual] shortcut-binding executed: ...`, `[NeoVisual] toolwindow-enter-input` / `toolwindow-exit-input`. No new unit tests (VS-coupled).
- **Fails-if:** a key `HandleKey` can act on is no longer interesting (e.g. Ctrl+H in the editor no longer fires `[NeoVisual] shortcut-binding executed: Ctrl+H` — the modifier-chord check was dropped); the hook's `IsInteresting` no longer delegates (the switch is still hardcoded); `[NeoVisual] toolwindow-move key=...` no longer fires for hjkl in a tool window; `DefaultControllerKeys` misses a key the default controller acts on.

## BP-4 — X4: ProjectLayering (decision + documentation)

- **Files:**
  - MODIFY `docs/architecture-consolidation.md` (X4 section `:137` — record the decision)
  - MODIFY `docs/spec.md` (durable layering note)
- **Change:** **DECISION, not a code change.** Choose option **(a) — accept + document the seam explicitly** (F46's recommendation): the host (`MyExtension`) depends on `Telescope` for core infrastructure (`NeoVisualLog` — the extension-wide logger — and `TelescopeController`), and `Telescope.csproj` is itself VS-coupled (VS SDK `:11` + WPF refs `:15-21`); only `OverlayKeyHandler`/`TextMotionNavigator`/`FzfFilter`/`LogFileWriter` are actually VS-free. Document in `docs/architecture-consolidation.md` (X4 section `:137`): the seam is accepted, the "pure-logic library" framing is corrected, and the roadmap must NOT grow more VS-coupled code inside the "library" (new VS-coupled code goes in `MyExtension`; new pure logic may go in `Telescope`). Add a one-line note to `docs/spec.md` recording the same decision (durable + linted). Option (b) — moving the log trio + `DiagnosticLog` into a truly shared location — is **deferred** (a structural change that ripples through project references; out of scope for a behavior-preserving consolidation). The doc text must reference only existing symbols (`NeoVisualLog`, `TelescopeController`, `DiagnosticLog`, `LogFileWriter`, `OverlayKeyHandler`, `TextMotionNavigator`, `FzfFilter`, `Telescope.csproj`, `MyExtension.csproj`) so the doc-ref lint passes.
- **Verify-with:** `pwsh tools/check-doc-refs.ps1` exits 0 (the doc step must not introduce unresolvable backticked references); `dotnet build` succeeds (no code change, but the build stays green). No new unit tests.
- **Fails-if:** `pwsh tools/check-doc-refs.ps1` exits 1 (a backticked reference in the new doc text doesn't resolve); the decision is not recorded (no doc change).

---

## 3. Unit tests to add/update (RED proof)

All new seam tests follow the `OverlayKeyHandler`/`TextMotionNavigator` pattern: dependency-free
classes the UI delegates to. **RED proof** = the test references an API that does not exist yet
(compile error) or asserts behavior the current code does not have (assertion failure); GREEN
after the merge. Each test is added in the SAME commit as its merge (A4).

| Merge | Test(s) | Project | RED proof | GREEN after |
|-------|---------|---------|-----------|-------------|
| X1 (BP-1) | `Run_LogFileWriter_Buffered_NotFlushedYet`, `Run_LogFileWriter_Buffered_FlushWritesToDisk`, `Run_LogFileWriter_Buffered_ContentIdenticalToAppend`, `Run_LogFileWriter_Buffered_FlushOnClose`, `Run_LogFileWriter_Buffered_PathChangeReopens` | `tests/Telescope.Tests` | `Flush()`/`Close()` don't exist → compile error; `NotFlushedYet` fails because the current code writes immediately (`File.AppendAllText` at `LogFileWriter.cs:95`) | `Write` buffers; `Flush()`/`Close()` write to disk; content byte-identical to the per-call append; path change reopens the writer |
| X1 (BP-1) | **UPDATE** `Run_LogFileWriter_WritesAndClearsFile` (`tests/Telescope.Tests/Program.cs:114-146`) | `tests/Telescope.Tests` | (A4 — updated in the same commit) | passes with `LogFileWriter.Flush()` added after the `Write`/`WriteDebug` calls and before the `File.ReadAllText` assertions; `Clear()` + empty-file assertions stay |
| X1 (BP-1) | **UPDATE** `Run_TelescopeLog_Prefix` + `Run_TelescopeLog_EmptyMessage` (added in Lane 1 BP-6) | `tests/Telescope.Tests` | (A4 — updated in the same commit; F3) | pass with `LogFileWriter.Flush()` added after the `TelescopeLog.Log(...)` call and before the `File.ReadAllText` assertions (buffering breaks them without the flush) |

**Existing tests that must stay green (no change):** all 56 `tests/Telescope.Tests` (incl.
`Run_LogPrefixes_Pinned` at `:940-947` — the `[GlobalKeyboard] ` constant assert at `:946` stays,
the constant is kept) and all 38 `tests/NeoVisual.Tests`. **Constraint:** the new buffered tests
must NOT call `Clear()` — the once-per-process `_clearedThisProcess` flag is consumed by
`Run_LogFileWriter_WritesAndClearsFile`.

---

## 4. E2E gate (deferred — informational only)

No NEW e2e scenarios are needed — every merge is behavior-preserving, so the e2e gate is the
**existing scenarios staying green**. The affected existing scenarios per merge (from the plan's
§10 Lane 5 queue) are QUEUED in `e2e-queue.md` and NOT executed on this machine:

- **X1:** full 35-scenario suite (log lines are the contract) — the buffered write path must not
  change WHEN lines appear beyond the ~200ms flush interval, and `FileShare.Read` must keep the
  harness's `Get-Content` working while VS holds the log open. **e2e-only risk (rev3):** `NeoVisualLog.Debug` routes ~51 error-path `Debug.WriteLine` sites through the full facade, so error lines now appear in the structured exp log where they did not — the unit gate cannot verify the harness's regex patterns still match only intended lines; the deferred e2e must re-verify no pattern collision (error lines are `... failed: ...`).
- **X2:** e2e suite (the harness refactor must not change scenario behavior).
- **C8:** neovisual-toolwindow/textinput e2e.
- **X4:** build + unit suites.

Each asserts: the listed scenarios stay GREEN + `git diff` shows no log-literal drift.
Diagnostics depended on: the existing `[Telescope]`/`[NeoVisual]`/`[Hook]` lines (unchanged).

---

## 5. Verification Trace (Lane 5)

| failing test / scenario | implicated steps | expected diagnostic / pass signal |
|---|---|---|
| `Run_LogFileWriter_Buffered_*` (RED: buffering doesn't exist — `Flush()`/`Close()` don't compile; `NotFlushedYet` fails because the current code writes immediately) | BP-1 | `LogFileWriter.Flush()` writes buffered lines to disk; content byte-identical to the per-call append; `Close()` flushes; path change reopens the writer |
| `Run_LogFileWriter_WritesAndClearsFile` (updated: `Flush()` before assertions) | BP-1 | passes with the buffered writer (A4) |
| `Run_TelescopeLog_Prefix` / `Run_TelescopeLog_EmptyMessage` (updated: `Flush()` before assertions) | BP-1 | pass with the buffered writer (A4, F3) |
| `Run_LogPrefixes_Pinned` | BP-1 | `[NeoVisual] ` / `[Telescope] ` / `[Hook] ` / `[MyExtension] ` / `[GlobalKeyboard] ` unchanged (constant kept) |
| `tests/Telescope.Tests` (72→77) | BP-1..BP-4 | all existing `Run_*` pass |
| `tests/NeoVisual.Tests` (73) | BP-1..BP-4 | all existing `Run_*` pass |
| `pwsh tools/test-e2e.ps1 -List` | BP-2 | lists all 35 scenarios, exit 0 |
| harness parse checks (iterate-telescope.ps1, dte-command.ps1) + harness-common dot-source | BP-2 | parse OK; `Close-Telescope` present |
| `pwsh tools/check-doc-refs.ps1` | BP-4 | exit 0 (doc-ref lint) |
| A3 log-literal check (`git diff`) | BP-1..BP-4 | no `[Telescope]`/`[NeoVisual]`/`[Hook]` **structured-log** literal changed |

**Known-RED allowlist (do NOT report as regressions):** none — `docs/progress.md` reports no
known-RED e2e scenario remains (all 35 green as of 2026-09-27); e2e is deferred to
`e2e-queue.md` anyway.

**Documented deviations (adjudicated, not regressions):**
1. **BP-1:** the hook's install/uninstall lines change prefix `[GlobalKeyboard]` → `[Hook]` (the
   "one `[Hook]` prefix" target). `DiagnosticLog.GlobalKeyboard` (`DiagnosticLog.cs:15`) is KEPT
   (still pinned by `Run_LogPrefixes_Pinned`) but is no longer emitted by the hook. The harness
   never asserts on `[GlobalKeyboard]` (grep-verified: no `[GlobalKeyboard]` in `tools/`). No
   `[Telescope]`/`[NeoVisual]`/`[Hook]` literal changes anywhere.
2. **BP-1:** `NeoVisualLog.Debug(msg)` routes error-path diagnostics through the full facade
   pipeline (structured exp log + pane + debug file) — previously debug-file only. Text
   byte-identical; write-path change only. No asserted pattern collides (error lines are
   `... failed: ...`).

---

## KEY DECISIONS (do not second-guess)

- **BP-1:** `NeoVisualLog.Debug(msg)` is a documented alias for `Log` (`=> Log(message)`) — do
  NOT write `Debug.WriteLine(msg) + NeoVisualLog.Log(msg)` literally (`Log` already calls
  `Debug.WriteLine` internally at `NeoVisualLog.cs:97` → double-write to the debug file).
- **BP-1:** the buffered `LogFileWriter` writer MUST open with `FileShare.Read` (the e2e harness
  reads the log file while VS holds it open) and UTF-8 without BOM (matches `File.AppendAllText`
  → byte-identical file content). The ~200ms flush timer keeps lines on disk within one harness
  poll; the `NotFlushedYet` test asserts within microseconds of `Write` (negligible race; if
  flaky, raise the interval to 500ms).
- **BP-1:** `DiagnosticLog.GlobalKeyboard` is KEPT (pinned by `Run_LogPrefixes_Pinned`) even
  though the hook stops emitting it — do not delete it or update the pinned test.
- **BP-1:** the new buffered tests must NOT call `Clear()` — the once-per-process
  `_clearedThisProcess` flag is consumed by `Run_LogFileWriter_WritesAndClearsFile`.
- **BP-3:** `IsKeyOfInterest` MUST preserve the modifier-chord check (`ctrl || shift || alt`) —
  the task's formula omits it, but without it Ctrl+H/Ctrl+W simple shortcuts never reach
  `HandleKey` (a regression). The pre-filter stays cheap (in-process property reads only; no
  per-key logging — F2 not reintroduced).
- **BP-4:** choose option (a) — accept + document the seam (F46's recommendation); option (b)
  (moving the log trio + `DiagnosticLog` into a truly shared location) is deferred as a
  structural change out of scope for a behavior-preserving consolidation.
- **Doc-ref updates are mandatory** in BP-4 — `pwsh tools/check-doc-refs.ps1` is the acceptance
  gate for that step; the new doc text must reference only existing symbols.


---


---

# Cross-cutting sections

## Acceptance criteria (whole program)

| # | Criterion | Diagnostic asserted | Test |
|---|-----------|--------------------|------|
| A1 | Every extracted pure seam ships with NEW unit tests that are RED before the merge and GREEN after | the seam's diagnostic unchanged (e.g. `[Telescope] grep hits=...`, `[NeoVisual] navigate direction=...`) | the new seam tests (Lane 1: `Run_ActionsRegistry_*`/`Run_TelescopeLog_*`/`Run_CaretPlacement_*`/`Run_GetCandidates_DefaultQuery_*`; Lane 2: `Run_TextMotionEngine_MapMotion_*`/`Run_ActionTable_*`/`Run_FocusGuard_TruthTable_*`; Lane 3: `Run_FileLocation_*`/`Run_IFileLocation_*`/`Run_FinderBase_*`; Lane 4: `Run_RectCoordinate_*`/`Run_NavigationSettings_FromDpi`/`Run_WindowNavigationEngine_*`; Lane 5: `Run_LogFileWriter_Buffered_*`) |
| A2 | Both offline unit suites stay green after every lane | suite exit code 0 | `dotnet run --project tests/Telescope.Tests` (56 → 63 after L1 → 72 after L3 → **77 after L5**) + `tests/NeoVisual.Tests` (38 → 42 after L1 → 58 after L2 → **73 after L4**) |
| A3 | NO `[Telescope]`/`[NeoVisual]`/`[Hook]` **structured-log** literal changes anywhere | `git diff` shows no change to any structured-log literal | the A3 log-literal check in each lane's Verification Trace (`git diff` shows no structured-log literal changed) |
| A4 | Every merge that changes a public/internal signature updates the affected unit tests in the same commit | the updated tests compile + pass in the same commit | per-merge unit test updates (e.g. `Run_RectCoordinate_StoresFields`, `Run_FileFinder_*` payload, `Run_LogFileWriter_WritesAndClearsFile`, `Run_TelescopeLog_*`) |
| A5 | The dead code / duplication targets are actually removed (no zombie seams) | `git diff` shows the deleted members | `git diff` shows the deleted members (IVsFrameView forwards, RemoveWindowsNotAdjacent, ActivateWindow, IQueryFinder, dead constants, etc.) |
| A6 | The doc-ref diff gate passes after any doc touch (no NEW unresolved backticked refs; the baseline lint is already red on 7 pre-existing refs — see the header) | `git diff` on the touched docs adds no unresolved backticked token | doc-ref steps (Lane 1 BP-3/BP-8, Lane 2 BP-1, Lane 4 BP-2/BP-3/BP-4, Lane 5 BP-4) |
| A7 | The deferred e2e gates (per lane) are queued in `e2e-queue.md` and NOT executed on this machine | e2e-queue.md QUEUED entries present | e2e-queue.md entries (QUEUED) |

## Unit test plan (RED proof, whole program)

All new seam tests follow the `OverlayKeyHandler`/`TextMotionNavigator` pattern:
dependency-free classes the UI delegates to. **RED proof** = the test references
an API that does not exist yet (compile error) or asserts behavior the current
code does not have (assertion failure); GREEN after the merge. Each test is added
in the SAME commit as its merge (A4). Full per-lane test tables live in each lane
section; the summary:

- **tests/NeoVisual.Tests** (add ~35, update ~8):
  - Lane 1: `Run_ActionsRegistry_*` (C3).
  - Lane 2: `Run_TextMotionEngine_MapMotion_*` (T1) + update `Run_TextInput_MapMotions`/`Run_TextInput_MapInsertMotions`; `Run_ActionTable_*` (T2); `Run_FocusGuard_TruthTable_*` (T5) + update the 5 existing FocusGuard tests.
  - Lane 4: `Run_RectCoordinate_*` (N2, incl. UPDATE `Run_RectCoordinate_StoresFields`); `Run_NavigationSettings_FromDpi` (N4); `Run_WindowNavigationEngine_*` (N3 — the core navigation algorithm becomes unit-testable).
- **tests/Telescope.Tests** (add ~20, update ~4):
  - Lane 1: `Run_TelescopeLog_*` (L5); `Run_CaretPlacement_*` (L7); `Run_GetCandidates_DefaultQuery_*` (L6).
  - Lane 3: `Run_FileLocation_*` + `Run_IFileLocation_*` (L3); `Run_FinderBase_*` (L2) + update `Run_FileFinder_*` payload.
  - Lane 5: `Run_LogFileWriter_Buffered_*` (X1) + update `Run_LogFileWriter_WritesAndClearsFile`.

## Diagnostics (log contract)

The verification contract (§8 of the doc) is absolute: **no change to any
`[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal.** Every merge is
behavior-preserving; the diagnostics the e2e harness asserts on are unchanged.
Two **documented deviations** (adjudicated, not regressions) are carried in the
lane Verification Traces and must NOT be reported as regressions:
1. **Lane 1 BP-3 (C2):** the 5 distinct `[NeoVisual]Failed to open Telescope ...`
   Debug.WriteLine error-path literals collapse into one parameterized form —
   failure-path only, never asserted by the harness, not in the structured exp log.
2. **Lane 5 BP-1 (X1):** the hook's install/uninstall lines change prefix
   `[GlobalKeyboard]` → `[Hook]` (the "one `[Hook]` prefix" target);
   `DiagnosticLog.GlobalKeyboard` is KEPT (pinned by `Run_LogPrefixes_Pinned`) but
   no longer emitted by the hook. The harness never asserts on `[GlobalKeyboard]`.

X1 (LogPath) is the only lane that touches the log *write path* — it must change
the write mechanism, never the emitted text.

## Known-RED allowlist

- **None.** `docs/progress.md` reports no known-RED e2e scenario remains (all 35
  green as of 2026-09-27). e2e is deferred to the queue anyway; the unit-only
  gate is the two offline suites + the new seam tests.

## E2E queue reference (deferred — see `e2e-queue.md`)

No NEW e2e scenarios are needed — every merge is behavior-preserving, so the e2e
gate is the **existing scenarios staying green**. Five queue entries (one per
lane), each listing the affected existing scenarios to re-run on a capable
machine after the plan is GREEN:

- **Lane 1:** hook install + navigation + telescope-open (C1); telescope-references/
  implementation + telescope-issues/grep (C6+L4); all telescope-* (C2);
  neovisual-leader (C3); telescope-references/implementation (C4); e2e log
  contract (L5); telescope-prompt-motions (L7); telescope-grep (L6); unit suites (X3).
- **Lane 2:** neovisual-textinput-motions; explorer-open-searchbox; **explorer-open-navigation (T3's gate)**; all neovisual-explorer-*; **full existing suite (T4 — ControllerBase touches all three controllers)**.
- **Lane 3:** all telescope-* finder scenarios (open-file/issues/grep/references/
  implementation/preview); telescope-references/implementation (C5);
  neovisual-toolwindow/explorer-* (C7).
- **Lane 4:** neovisual-window-nav; neovisual-toolwindow.
- **Lane 5:** full 35-scenario suite (X1 touches the log contract); e2e suite (X2);
  neovisual-toolwindow/textinput (C8); build + unit suites (X4).

Each asserts: the listed scenarios stay GREEN + `git diff` shows no log-literal
drift. Diagnostics depended on: the existing `[Telescope]`/`[NeoVisual]`/`[Hook]`
lines (unchanged).

## Queue reconciliation (Step 2 — applied at handoff, on approval)

The consolidation plan **subsumes** a large subset of the filed backlogs. At
handoff the new plan becomes the FIRST pending item, and the following filed
findings are marked **covered-by the consolidation plan** (not double-executed):

- **Architecture review backlog:** F2 (X1/C8), F3 (T1/L7), F4 (T1), F6 (N3),
  F7 (N1), F11 (L2), F36 (X3), F37 (X2), F38 (X2), F39 (X2), F40 (X2),
  F41 (X2), F44 (X2), F46 (X4). *(F13 is NOT subsumed — the plan never maps
  `FileFinder` to `ProjectFiles.Enumerate`; it stays in the backlog.)*
- **Code review backlog:** M4 (C2), M5 (C5/C6), M8 (N3), M9 (T1), and the minors
  m17 (N1/N2), m18 (N1), m19 (N1), m21 (N3), m23 (N1), m25 (N2), m33 (T1),
  m37 (T5), m38 (T1), m43 (T1/L7), m44 (L4), m45 (X1). *(NOT subsumed — the plan
  does not actually fix them: m6 `_package` (C1 doesn't touch it), m8 WindowManager
  cast (C7 doesn't touch it), m16 per-monitor DPI (N4 preserves system DPI),
  m20 CheckDte (intentionally left — see Lane 4 KEY DECISIONS), m22 dead
  null-activeWindow guard (N3 keeps it), m24 CompareWindows caption heuristic
  (N1 keeps it), m34 I/A insert placements (L7 says "do not fix"), m36
  ThrowIfNotOnUIThread (T2 doesn't add the guards), m59 Start-Sleep (X2 doesn't
  replace it).)*
- **Nits now covered:** n2 (`DefaultLogicalYWindowDivide` unused) → N4; n3
  (`ThrowOnFailure` in `IVsUIWindowFrameExtractor.cs:31`) → N1 (dies with the file).

**NOT subsumed** (stay in the backlog for later items): F5, F8, F9, F12, F13,
F14, F15, F22, F43, M19, m6, m7, m8, m9, m10, m11, m12, m13, m16, m20, m22, m24,
m32, m34, m35, m36, m46, m47, m48, m58, m59, m60, m62, m63, m64, m65, n1, n4-n16.

## Verification Trace (whole program)

| failing test / scenario | implicated lane | expected diagnostic / pass signal |
|---|---|---|
| `Run_ActionsRegistry_*` / `Run_TelescopeLog_*` / `Run_CaretPlacement_*` / `Run_GetCandidates_DefaultQuery_*` (RED before each merge) | Lane 1 (BP-4/BP-6/BP-7/BP-8) | GREEN after the merge; the seam's diagnostic unchanged |
| `Run_TextMotionEngine_MapMotion_*` / `Run_ActionTable_*` / `Run_FocusGuard_TruthTable_*` (RED before each merge) | Lane 2 (BP-1/BP-2/BP-4) | GREEN after the merge; `[NeoVisual] text-motion key=...` / `toolwindow-move key=...` unchanged |
| `Run_FileLocation_*` / `Run_IFileLocation_*` / `Run_FinderBase_*` (RED before each merge) | Lane 3 (BP-1/BP-2) | GREEN after the merge; `[Telescope] opened file: ...` / `grep hits=...` unchanged |
| `Run_RectCoordinate_*` / `Run_NavigationSettings_FromDpi` / `Run_WindowNavigationEngine_*` (RED before each merge) | Lane 4 (BP-1/BP-2/BP-3) | GREEN after the merge; `[NeoVisual] navigate direction=...` unchanged |
| `Run_LogFileWriter_Buffered_*` (RED before the merge) | Lane 5 (BP-1) | GREEN after the merge; log content byte-identical to the per-call append |
| `tests/Telescope.Tests` (56→77) | all lanes | all existing `Run_*` pass |
| `tests/NeoVisual.Tests` (38→73) | all lanes | all existing `Run_*` pass |
| `pwsh tools/check-doc-refs.ps1` | the doc-ref steps (L1 BP-3/BP-8, L2 BP-1, L4 BP-2/BP-3/BP-4, L5 BP-4) | exit 0 |
| A3 log-literal check (`git diff`) | all lanes | no `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal changed |
| e2e gates (deferred, queued in `e2e-queue.md`) | all lanes | the affected existing scenarios stay green on a capable machine |

**Known-RED allowlist (do NOT report as regressions):** none — `docs/progress.md`
reports no known-RED e2e scenario remains (all 35 green as of 2026-09-27); e2e is
deferred to `e2e-queue.md` anyway.

---

## Execution Log

(empty — populated by `neovim_hub` per attempt; each lane's attempt is recorded
here with `delegations: | VS boots: | iterations:` cost lines.)


---


