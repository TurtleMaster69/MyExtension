# MyExtension — Code Review (code-review-hub)

- **Date:** 2026-09-29
- **Scope:** whole repo — `MyExtension/` (core + `CardinalMovment/` + `ToolWindows/`), `Telescope/`, `tests/`, `tools/`, and the workflow docs. **10-round review** of the post-consolidation codebase (Architecture consolidation `b945ab8` + F22/F12/F15 + `KeyInjection.SimulateOnly`).
- **Method:** 10 rounds × 10 parallel workers (5 correctness rounds with `code-review-worker`, 5 simplification rounds with `arch-auditor`), one worker per 10-section slice (MyExtension core / CardinalMovment / ToolWindows / VimModeTracker+WM+Package / Telescope overlay / Telescope finders / Telescope preview-motion-fzf / Telescope logging / tests / tools). A whole-repo `trailmark-recon` digest (1298 nodes, 552 proxies, 0 entrypoints) was shared with every worker; each structural claim was verified with Trailmark (`QueryEngine.from_directory(..., language="c_sharp")`, proxy-aware `callers_of`). ~784 raw findings were consolidated into the canonical findings below; every high-impact NEW claim was spot-verified against current code by the hub (one R10 claim — `InstallDebugListener` "no callers" — was **refuted** and dropped). No file under `MyExtension/`, `Telescope/`, `tests/`, `tools/` was modified.

## Summary

**75 findings: 3 critical, 30 major, 30 minor, 12 nit.** The consolidation landed well — the pure state machines (`OverlayKeyHandler`, `TextMotionNavigator`, `LeaderSequenceMatcher`, `FocusGuard`, `HierarchyResolver`, `WindowNavigationEngine`, `FinderBase`, `ResultMapper`, `ProjectFileCache`) are extracted and unit-tested, the UI-thread hook discipline holds, net472 compliance is clean, and the diagnostics-as-contract logging is consistent. The three criticals are all **regressions or latent breaks introduced by the consolidation itself** and must be fixed before the next e2e gate: the per-type controller loop silently overwrites `SolutionExplorerController` (all `neovisual-explorer-*` scenarios would regress), the e2e runner still reports PASS on a `$false`-returning scenario (F38 was only half-fixed), and the deferred `ShowDialog` can fire after `CloseOverlay` closed the window.

The sharp edges are the same **scalability and duplication clusters** the prior review flagged, now re-verified against current code: (1) the **hook hot path still violates its own "cheap pre-filter" contract** — a per-key `[Hook]` log write, a per-key `File.Exists` sentinel probe, and up to 3× the high-complexity `IsTextInputType` classification per keystroke; (2) the **Telescope finder path stalls the UI** — fzf spawned per keystroke with no timeout, GrepFinder re-walks the DTE tree and re-reads every file per keystroke bypassing `ProjectFileCache`, and the preview re-walks the whole `FlowDocument` per caret move; (3) a **duplication cluster** that the merge-systems round mapped precisely (KeyToString ×2, DTE walkers ×3, vim-motion dispatch ×3, caret renderers ×3, focus keepers ×2, log writers ×4, harness open/wait helpers ×5/×3); and (4) a **testability gap** — the shortcut-matching path, the VsVim reflection interop, the mode classifier, and the overlay focus-target state machine are all still buried in VS/WPF-coupled code with no pure seam.

## Findings table

| id | sev | file:line | problem |
|----|-----|-----------|---------|
| CR1 | critical | `MyExtension/MyExtensionPackage.cs:92-101` | Per-type controller loop registers a `GeneralToolWindowController` for EVERY `ToolWindowType` incl. `SolutionExplorer`, overwriting the `SolutionExplorerController` registered at `:87` (dict assignment `WindowManager.cs:78`) |
| CR2 | critical | `tools/test-e2e.ps1:1907-1911` | F38 half-fixed: `$ok = ($result -ne $false)` but `$failures` appended only in `catch` → a `$false`-returning scenario prints FAILED yet bypasses the `:1915` aggregate gate → "RESULT: PASS" + exit 0 |
| CR3 | critical | `Telescope/TelescopeOverlay.cs:303` | Fire-and-forget `Dispatcher.BeginInvoke(ShowDialog)` can run after `CloseOverlay()`'s `Close()` → `ShowDialog` throws on an already-closed window |
| M1 | major | `MyExtension/GlobalKeyboardHook.cs:124` | Per-key `[Hook] key=...` log write (file + pane + Debug) on the UI thread inside the hook callback — not a harness contract (ref F2) |
| M2 | major | `MyExtension/CardinalMovment/WindowMatrix.cs:105-106` | Per-keystroke N+1 `GetWindowScreenRect` COM calls (active rect fetched twice + once per window) |
| M3 | major | `MyExtension/WindowManager.cs:44-49` | `IsTestStaleInjected()` runs `File.Exists` on every `IsToolWindow`/`Type` read — i.e. per key on the hook path in every harness run |
| M4 | major | `MyExtension/InputHandler.cs:283,393,423` | `GeneralToolWindowController.IsTextInputType` (complexity 19) invoked up to 3× per key-down |
| M5 | major | `Telescope/GrepFinder.cs:80,130` | Per-keystroke full DTE solution-tree walk + `File.ReadAllLines` of every file, bypassing `ProjectFileCache` (only CodeIssuesFinder uses it) |
| M6 | major | `Telescope/FzfFilter.cs:91,133` | Per-keystroke fzf subprocess spawn with no timeout; silent fallback to full list on failure; `IsAvailable` never wired into production (ref F8/F9) |
| M7 | major | `Telescope/PreviewRenderer.cs:122` | `CaretToPointer` re-walks the whole `FlowDocument` (blocks×runs) on every caret move — O(n) per `j/k/w/b/e` keystroke |
| M8 | major | `Telescope/TelescopeOverlay.cs:349` | Fire-and-forget `_ = FilterAndUpdateAsync(...)` with no try/catch → unobserved task exception if fzf spawn fails (ref F27) |
| M9 | major | `Telescope/SyntaxHighlighter.cs:198` | `ReadQuoted` `i += 2` overshoot past the closing quote on `""`/escaped sequences |
| M10 | major | `Telescope/TextMotionNavigator.cs:120` | `Up()` uses `LastIndexOf` with a negative `startIndex` when the caret is on line 0 |
| M11 | major | `Telescope/PreviewRenderer.cs:141` | `CaretToPointer` returns null on a blank line → caret placement no-op |
| M12 | major | `MyExtension/CardinalMovment/WindowMatrix.cs:98,106` | `AutoHides()` outside try/catch; one bad frame aborts the whole navigation (ref F7) |
| M13 | major | `Telescope/CodeIssuesFinder.cs:186` | `Classify()` guesses kind by substring on the description instead of `ErrorItem.Severity` → mislabeled `[ERR]`/`[WARN]` rows |
| M14 | major | `MyExtension/ToolWindows/SolutionExplorerController.cs:295` | `BuildForest` nested-folder expansion gap (which kinds recurse, `.cs` filter) |
| M15 | major | `MyExtension/LeaderSequenceMatcher.cs:63` | `action()` invoked with no try/catch inside the hook path — a binding handler exception escapes into the hook callback |
| M16 | major | `MyExtension/ToolWindows/FocusGuard.cs:26` | `isTextInputSurface` reverse-leak: a text-input surface is never vetoed even when the editor holds focus |
| M17 | major | `MyExtension/VimModeTracker.cs:189,209,483-488` | Stale typing flag on focus loss, `_resolved` latch, `(int)value` cast on a null out-param (ref F5) |
| M18 | major | `Telescope/NeoVisualLog.cs:109,132-148` | Logging races on the shared writer; `WriteToPane` swallows pane failures silently |
| M19 | major | `tools/dte-command.ps1:92` | No-timeout COM call — a hung `Debug.Start` blocks the harness forever |
| M20 | major | `tests/Telescope.Tests/Program.cs:149,286,774` | Order-dependent LogFileWriter tests; fzf test silently passes when fzf absent; keybindings test reads the user's real config (ref F43/F45) |
| M21 | major | `MyExtension/InputHandler.cs:469` + `MyExtension/LeaderSequenceMatcher.cs:97` | `KeyToString` duplicated verbatim — config key-name contract can drift between the shortcut and leader paths |
| M22 | major | `Telescope/FileFinder.cs:103` + `MyExtension/MyExtensionPackage.cs:285` | 3rd DTE walker: `FileFinder.CollectProjectFiles` and `FindFirstSourceFileInItems` duplicate `ProjectFiles.Enumerate` (ref F13) |
| M23 | major | `MyExtension/MyExtensionPackage.cs:442-466` | `OpenReference`/`OpenImplementation` byte-identical (File.Exists guard + `OpenFileAtLine`) |
| M24 | major | `Telescope/TelescopeOverlay.cs:566,606` + `MyExtension/ToolWindows/TextMotionHelper.cs:70` | Vim key→motion dispatch triplicated (prompt/preview/tool-window); `$` already drifted (Shift-gated in prompt, bare in preview) (ref F3) |
| M25 | major | `MyExtension/BlockCaretAdornment.cs:24` + `Telescope/TelescopeOverlay.cs:593` | Block caret rendered in 3 places (adornment / prompt style / TextMotionHelper) (ref F4) |
| M26 | major | `MyExtension/ToolWindows/SolutionExplorerController.cs:114,241` | Focus-keeper `DispatcherTimer` idiom duplicated (100ms/1500ms) — already diverged |
| M27 | major | `Telescope/NeoVisualLog.cs:79` + `Telescope/LogFileWriter.cs:103` | Log pipeline has 4 overlapping entry points: `NeoVisualLog.Debug` is a byte-identical alias of `Log`; `Write`/`WriteDebug` byte-identical; `NeoVisualTraceListener.Write`/`WriteLine` byte-identical |
| M28 | major | `tools/test-e2e.ps1:259,286,308,331,355` + `tools/harness-common.ps1:134,154,175` | Harness duplication: 5 `Open-Telescope*` copies, 9× SE-toggle loop, 3 near-identical wait helpers, 2× seed canonical content |
| M29 | major | `tests/Telescope.Tests/Program.cs:110,113` + `tests/NeoVisual.Tests/Program.cs:262` | Test scaffolding copy-pasted: temp-dir setup 23×, LogFileWriter path save/restore 8×, duplicate MapMotion test groups |
| M30 | major | `MyExtension/Actions.cs:22` + `MyExtension/TelescopeLauncher.cs:25` | Action-name→finder-name mapping split across two dictionaries keyed by the same strings — a typo throws `KeyNotFoundException` in the hook path |
| M31 | major | `MyExtension/InputHandler.cs:346-353` | Simple-shortcut matching (Ctrl+H etc.) buried in the VS-coupled `HandleKey` — no pure seam (the `LeaderSequenceMatcher` pattern exists for this) |
| M32 | major | `MyExtension/VimModeTracker.cs:366,299-531` | Mode classifier + VsVim reflection interop not behind a pure seam — untestable without live VS + VsVim |
| M33 | major | `MyExtension/MyExtensionPackage.cs:116-119` | `InitializeAsync` (blast radius 85) wraps ~10 steps in ONE catch-all — a failure names no step and partial state is invisible |
| M34 | major | `Telescope/TelescopeOverlay.cs:75` | Focus-target state machine (Ctrl+H/L, Escape-to-list) buried in the WPF `OnPreviewKeyDown` override — wrong seam |
| M35 | major | `Telescope/LogFileWriter.cs:112` | `Write`/`WriteDebug` swallow every exception — the log pipeline is undiagnosable when it fails (harness times out with no clue) |
| M36 | major | `tools/iterate-telescope.ps1:112,104` | Blanket `Get-Process devenv | Stop-Process -Force` kills the user's unrelated VS instances; USER-scope env mutation persists across sessions |
| M37 | major | `tools/test-e2e.ps1:73` | `$TimeoutSec = 300` declared but never enforced — a stuck scenario hangs the whole suite |
| M38 | major | `MyExtension/WindowManager.cs:9` | `WindowManager` declared in the GLOBAL namespace (no `namespace` block) while every sibling uses `namespace MyExtension` |
| M39 | major | `MyExtension/CardinalMovment/Direction.cs:8` | `Axis()` returns the PERPENDICULAR axis (Up→X), the opposite of what the name implies |
| M40 | major | `MyExtension/CardinalMovment/WindowMatrix.cs:94` | `NavigateInDirection(char)` takes magic `'U'/'D'/'L'/'R'` instead of the existing `Direction` enum |
| M41 | major | `Telescope/GrepFinder.cs:49` | `GatherHits()` stub always returns empty — the name lies about what it does (real logic is in `GetCandidates(string)`) |
| M42 | major | `tests/NeoVisual.Tests/Program.cs:109,117,262` | Test names lie: "StartInInsert/StartInNormal" only classify `IsTextInputType`; `TextInput_*` MapMotion tests duplicate `TextMotionEngine_*` |
| M43 | major | `Telescope/LogFileWriter.cs:206` | 200ms flush timer fires forever — `NeoVisualLog.Close` is never called in production, so the timer is never disposed |
| m1 | minor | `MyExtension/InputHandler.cs:451-461` | `BuildSimpleKey` allocates a `List<string>` + `string.Join` per interesting key on the hook path |
| m2 | minor | `MyExtension/MyExtensionPackage.cs:699-709` | `ReadLine` re-opens + re-enumerates the file per reference/implementation hit |
| m3 | minor | `MyExtension/ToolWindows/TextMotionHelper.cs:122,162` | Per motion key: full buffer copy (`snapshot.GetText()`) + fresh navigator allocation |
| m4 | minor | `MyExtension/VimModeTracker.cs:387` | `GetModeKindFromEventArgs` does per-event reflection (uncached `GetProperty("ModeKind")`) |
| m5 | minor | `MyExtension/MyExtensionPackage.cs:721` | `IsWriteLocation` does per-hit reflection (`GetProperty("IsWrittenTo")`) |
| m6 | minor | `MyExtension/VimModeTracker.cs:436` | `GetTextBuffer` uncached reflection, inconsistent with the file's own `??=` caching pattern |
| m7 | minor | `MyExtension/MyExtensionPackage.cs:136` | `GetServiceAsync(SVsShell)` inside the 40-iteration poll loop — up to 40 redundant lookups |
| m8 | minor | `MyExtension/WindowManager.cs:125` | `guid != null` on a `Guid` struct is always true → the `Unknown` fallback branch is dead |
| m9 | minor | `MyExtension/WindowManager.cs:103` | `RefreshCurrentWindow` touches `IVsMonitorSelection` without `ThrowIfNotOnUIThread()` |
| m10 | minor | `MyExtension/InjectedKeyGuard.cs:42-67` | Pending per-VK counter never expires — a leaked record permanently disables that key's action |
| m11 | minor | `MyExtension/KeybindingConfig.cs:91-102,187-194` | Malformed `keybindings.json` / invalid leader silently swallowed (Debug-only) → user's bindings vanish with no feedback |
| m12 | minor | `MyExtension/LeaderSequenceMatcher.cs:63` | `LeaderResult.Execute` carries the action back but the matcher also invokes it internally — ambiguous side-effect ownership |
| m13 | minor | `Telescope/TelescopeOverlay.cs:746` + `Telescope/FinderBase.cs:37,60` + `Telescope/TelescopeController.cs:56` | `[Telescope]` prefix hand-concatenated at 5+ sites via `NeoVisualLog.Debug` instead of the centralized `TelescopeLog.Log` |
| m14 | minor | `Telescope/ResultMapper.cs:40` | Unmatched display string falls back to a null-payload entry that silently no-ops on select |
| m15 | minor | `Telescope/TelescopeOverlay.cs:474,568` | Fresh `TextMotionNavigator` allocated per keystroke (prompt) instead of reusing a field like `_previewNavigator` |
| m16 | minor | `Telescope/PreviewRenderer.cs:109` | `ColorFor` allocates a new `SolidColorBrush` per run per preview render |
| m17 | minor | `Telescope/TextMotionNavigator.cs:37` | `LineNumber`/`ColumnNumber` are O(n) scans recomputed on every access |
| m18 | minor | `Telescope/PreviewRenderer.cs:27` | `Show`/`SetContent` do `ReadAllText` + full tokenize + `FlowDocument` rebuild synchronously on the UI thread per selection |
| m19 | minor | `Telescope/PreviewRenderer.cs:35` | `preview file=... chars=...` undocumented `chars=` suffix; `preview caret=` line emitted only when `LineNumber > 0` |
| m20 | minor | `Telescope/CodeIssuesFinder.cs:182` + `Telescope/GrepFinder.cs:92` | Finder error diagnostics bypass `TelescopeLog.Log` (manual prefix + `NeoVisualLog.Debug`) — 3 error-reporting mechanisms in one slice |
| m21 | minor | `Telescope/CodeIssuesFinder.cs:125` | `CollectTodos` re-reads every project file on every open (cache saves the walk, not the I/O) |
| m22 | minor | `Telescope/ProjectFileCache.cs:15` | Cache invalidated only on solution-name change — a mid-session file add never appears |
| m23 | minor | `Telescope/LogFileWriter.cs:88` | `Clear()` once-per-process vs `ShowOverlay` Clear-on-open — logs accumulate across opens (ref F28) |
| m24 | minor | `Telescope/NeoVisualLog.cs:60-61` | `-main.log`/`-exp.log` suffix inversion: the structured log the harness asserts on is "exp", the raw debug stream is "main" |
| m25 | minor | `Telescope/DiagnosticLog.cs:9` | Extension-wide prefix constants live in the `Telescope` namespace/folder (layering smell) |
| m26 | minor | `tools/test-e2e.ps1:831,938` | `Wait-NewLogLineAfter` gets a `Get-Content` line-count as a cache index — truncation makes the wait time out (false failure) |
| m27 | minor | `tools/check-doc-refs.ps1:199-201,171-179` | Missing doc silently skipped; `Test-ToolFunctionExists` regex-matches whole-file text (false negative) |
| m28 | minor | `tools/test-e2e.ps1:1027` | Editor-focus absence scan mixes a cache index with a fresh `Get-Content` array — truncation silently passes the leak guard |
| m29 | minor | `tools/iterate-telescope.ps1:78` | Scratch seeding gated on `Test-Path` — a stale scratch from a prior run is reused (test-e2e.ps1 always resets) |
| m30 | minor | `tests/NeoVisual.Tests/Program.cs:468,479,487,503,548,555` | Magic `actionKeyCount: 5` in FocusGuard tests with no explanation |
| n1 | nit | `MyExtension/MyExtensionPackage.cs:44` | `_keyboardLogger` field holds a `GlobalKeyboardHook`, not a logger (stale name) |
| n2 | nit | `MyExtension/MyExtensionPackage.cs:51` + `MyExtension/MyExtensionPackage.cs:693` | `[MyExtension]` vs `[NeoVisual]` prefix split; `GetCaretOffset` uses `[Telescope]` inside the package |
| n3 | nit | `MyExtension/ToolWindows/SolutionExplorerController.cs:44-45` | Magic VK literals in the `toolwindow-move ... vk=40/38` log while the press uses `KeyInjection.VK_DOWN/VK_UP` |
| n4 | nit | `Telescope/NeoVisualLog.cs:28` | Placeholder pane GUID `A1B2C3D4-...` + magic `CreatePane(..., 1, 1)` args |
| n5 | nit | `tools/test-e2e.ps1:539,572,825,924,987` | Comments reference `neovascular-*` scenario names that don't exist (real: `neovisual-*`) |
| n6 | nit | `tools/test-e2e.ps1` (many) | ~100 raw hex VK codes (`0x4A`) with only 4 named constants |
| n7 | nit | `tools/iterate-telescope.ps1:48,68` | `$expHive` and `Find-VsWindow` dead (defined, never used) |
| n8 | nit | `MyExtension/CardinalMovment/WindowAdapter.cs:120` | `_rect` field write-only — `Rect` always re-fetches; misleading cache |
| n9 | nit | `MyExtension/CardinalMovment/WindowMatrix.cs:14` | Mixed field conventions (`m_ActiveWindows`, `m_activeWindow`, `_settings`) |
| n10 | nit | `Telescope/TextMotionNavigator.cs:155,207,210` | `LineStartHome` portmanteau; `InsertStart`/`InsertEnd` move to whole-text, not line (doc says "text/line") |
| n11 | nit | `Telescope/SyntaxHighlighter.cs:43,64` | `Keywords` PascalCase private field; `Segment(string)` verb/noun collision with the `SyntaxSegment` type |
| n12 | nit | `Telescope/PreviewRenderer.cs:60` | `segment.Text.Split('\n')` allocates a string[] per segment per render |

## Detailed findings

### CR1 (critical) — per-type controller loop overwrites `SolutionExplorerController`
- **Where:** `MyExtension/MyExtensionPackage.cs:87,92-101`; `MyExtension/WindowManager.cs:75-78`.
- **What's wrong:** `InitializeAsync` registers the special `SolutionExplorerController` at `:87`, then a `foreach (ToolWindowType type in Enum.GetValues(...))` loop at `:92-101` registers a `GeneralToolWindowController` (or `TextInputToolWindowController`) for **every** type — including `SolutionExplorer`. `RegisterController` does `_controllers[controller.Type] = controller` (dict assignment), so the loop **overwrites** the special controller. Confirmed by 4 independent workers + hub spot-check.
- **Why it bites:** Solution Explorer loses `o`/`Enter`/`r`/`m`/`a`/`g`/`i` — every `neovisual-explorer-*` e2e scenario regresses, and the `FocusGuard`/`EditorFocusedVeto` wiring that depends on `SolutionExplorerController` silently degrades to the generic hjkl controller. This is a consolidation-introduced regression (the loop was added to "register one controller per type").
- **Fix:** Delete the per-type loop's `SolutionExplorer` iteration (skip `type == ToolWindowType.SolutionExplorer`), or move the default registration into `WindowManager` (a `RegisterDefaults()` the ctor calls) and have the package register only the specials. Gate on the `neovisual-explorer-*` e2e scenarios.

### CR2 (critical) — e2e runner still reports PASS on a `$false`-returning scenario
- **Where:** `tools/test-e2e.ps1:1907-1911` (and the aggregate gate at `:1915`).
- **What's wrong:** The F38 fix computes `$ok = ($result -ne $false)` and prints per-scenario FAILED, but `$failures` is appended **only in the `catch`** block. A scenario that returns `$false` (instead of throwing) sets `$ok = $false` → prints "FAILED" → but the aggregate `if ($failures.Count -gt 0)` at `:1915` is bypassed → "RESULT: PASS" + exit 0.
- **Why it bites:** A broken scenario silently reports a green run — the exact false-PASS the harness exists to prevent. Any future scenario that returns `$false` on failure (the F38 pattern) is invisible to the gate.
- **Fix:** In the `else` branch (or after the try/catch), `$failures += "$name : returned false"` when `$result -eq $false`.

### CR3 (critical) — deferred `ShowDialog` can fire after `CloseOverlay`
- **Where:** `Telescope/TelescopeOverlay.cs:303` (`Dispatcher.BeginInvoke(new Action(() => ShowDialog()), ApplicationIdle)`), `:306-318` (`CloseOverlay` → `Close()`).
- **What's wrong:** `ShowDialog` is deferred to `ApplicationIdle` (fire-and-forget, no guard). If `CloseOverlay()` runs first (e.g. the overlay loses focus → `Deactivated` → `CloseOverlay`), the pending `ShowDialog` executes on an already-closed window and throws as an unhandled dispatcher exception.
- **Why it bites:** A stale open overlay closing on focus loss (the documented design) races the deferred show; the failure is an unhandled dispatcher exception with no diagnostic, and the race has no seam to unit-test.
- **Fix:** Guard the `BeginInvoke` with an `IsOpen`/closed check (e.g. capture a generation counter or check `IsVisible` before `ShowDialog`), and log around `ShowDialog`/`Close`.

### M1 — per-key `[Hook]` log write on the hook hot path
- **Where:** `MyExtension/GlobalKeyboardHook.cs:124`.
- **What's wrong:** Every "interesting" key-down (all modifier chords, Escape, leader, tool-window hjkl) runs string interpolation + a full `NeoVisualLog.Log` write (structured file + `Debug.WriteLine` + Output-pane COM) on the UI thread inside the low-level hook callback — before `HandleKey` even runs. The file's own `Log()` doc says it is "NOT for the per-key path", and AGENTS.md forbids per-key logging in the callback. `tools/` has no `[Hook] key=` assertion, so it is pure overhead, not a contract.
- **Why it bites:** Windows silently removes a low-level hook whose callback blocks too long; every Ctrl+C/V/Z/Shift-letter during normal editing pays a log write. This is the exact latency the pre-filter was built to avoid.
- **Fix:** Delete line 124, or gate it behind a debug-only flag / log only when `HandleKey` returns handled, at a coarser cadence.

### M2 — per-keystroke N+1 `GetWindowScreenRect` COM calls
- **Where:** `MyExtension/CardinalMovment/WindowMatrix.cs:105-106`; `WindowAdapter.cs:120-127`.
- **What's wrong:** `NavigateInDirection` reads `m_activeWindow.Rect` **and** `m_ActiveWindows.Select(w => w.Rect)` — each `Rect` access calls `RefreshRect` → `frame4.GetWindowScreenRect` (a COM round-trip). For 10-20 windows that is 11-21 COM calls per Ctrl+H/J/K/L, on the UI thread. The `Rect` comment claims "engine snapshots rects once per navigation" but it re-fetches per window per key.
- **Why it bites:** Navigation latency scales with window count; the active window's rect is fetched twice per key.
- **Fix:** Snapshot all rects once per `NavigateInDirection` (single pass), reuse the active rect from the snapshot, and run `SelectTarget` on the snapshot.

### M3 — `IsTestStaleInjected` runs `File.Exists` per key
- **Where:** `MyExtension/WindowManager.cs:44-49`.
- **What's wrong:** `IsTestStaleInjected()` calls `System.IO.File.Exists(TestStaleSentinelPath)` and is invoked from `IsToolWindow` (`:47`) and `Type` (`:49`), both read on the per-key hook path (`InputHandler.IsKeyOfInterest`/`HandleKey`/`HasToolWindowActionKeys`). In every harness run `NEOVISUAL_LOG_DIR` is set, so the sentinel path is non-null and each keystroke pays 2-3 filesystem syscalls on the UI thread.
- **Why it bites:** The "cheap pre-filter" (documented as a few Win32 calls) silently gains a filesystem stat per key in the exact harness runs used as the regression gate. The sentinel only toggles between scenarios, never mid-keystroke.
- **Fix:** Cache the sentinel result and refresh it only in `OnWindowFocusChanged` (where the stale-frame fault is relevant), not on every property read.

### M4 — `IsTextInputType` (complexity 19) up to 3× per key
- **Where:** `MyExtension/InputHandler.cs:283,393,423`; `GeneralToolWindowController.cs:71-96`.
- **What's wrong:** The high-complexity classification switch runs per keystroke even though the window type only changes on focus events.
- **Why it bites:** Repeated work on the hot path; a future window type added to the switch multiplies the per-key cost.
- **Fix:** Cache the text-input classification on `WindowManager` alongside `_type` (recompute in `OnWindowFocusChanged`) and read the cached bool per key.

### M5 — GrepFinder per-keystroke DTE walk + full file re-read
- **Where:** `Telescope/GrepFinder.cs:80,130`; `Telescope/ProjectFileCache.cs`.
- **What's wrong:** `GetCandidates` calls `ProjectFiles.Enumerate(dte)` (full DTE solution-tree walk) then `ScanFile` → `File.ReadAllLines(path)` for every file, on every debounced keystroke, on the UI thread. `ProjectFileCache` exists but is wired only into `CodeIssuesFinder.GatherHits`. The `HitCap=200` does not bound the walk (Enumerate returns all paths up front).
- **Why it bites:** On a large solution the overlay stutters per keystroke; the DTE walk is paid in full even when the cap is reached. This is the exact cost `ProjectFileCache` was built to amortize.
- **Fix:** Hoist `ProjectFileCache` to a shared/singleton (keyed by solution `FullName`) and route `GrepFinder.GetCandidates` through it; cache file contents (or the scan) for the overlay session.

### M6 — fzf per-keystroke spawn, no timeout, silent fallback
- **Where:** `Telescope/FzfFilter.cs:91,133,155`; `Telescope/TelescopeOverlay.cs:374`.
- **What's wrong:** `FilterAsync` spawns a fresh `fzf --filter` subprocess per keystroke with **no timeout** (unlike `IsAvailable`'s 3000ms `WaitForExit`); on any exception it swallows and returns the full unfiltered list with no `[Telescope]` log; `IsAvailable` (the documented "show a warning" path) is never called by production. `QuoteArg` hand-rolls Windows quoting (wrong for a trailing backslash).
- **Why it bites:** A missing/crashed fzf silently degrades to an unfiltered list on every keystroke with no warning; a hung fzf freezes the overlay's gather forever; a query ending in `\` is corrupted.
- **Fix:** Add a `Task.WhenAny`/`WaitForExit(timeout)` that kills the process and falls back; log `[Telescope] fzf filter failed: ...`; surface the `IsAvailable` warning; prefer fzf `--listen` or an in-process matcher behind the same seam.

### M7 — `CaretToPointer` O(n) re-walk per caret move
- **Where:** `Telescope/PreviewRenderer.cs:122`.
- **What's wrong:** Every preview caret move (`j/k/w/b/e/gg/G` → `ApplyPreviewCaret` → `ApplyCaret` → `CaretToPointer`) re-walks the entire `FlowDocument` (blocks→runs, +1 per non-last paragraph) from the start, and its index model must exactly mirror the paragraph-per-line layout built in `SetContent`.
- **Why it bites:** Large files (thousands of runs) pay O(n) per keystroke on the UI thread; any change to `SetContent`'s line-splitting silently desyncs the +1-per-line math, landing the caret on the wrong line (the e2e asserts `preview caret=... line=...`).
- **Fix:** Build a line-start index (or cache a `TextPointer` per line) once in `SetContent` and binary-search it in `CaretToPointer`.

### M8 — fire-and-forget filter with unobserved exceptions
- **Where:** `Telescope/TelescopeOverlay.cs:349,374`.
- **What's wrong:** `_ = FilterAndUpdateAsync(...)` has no try/catch around `_fzf.FilterAsync` or `RenderResults`; only the query-driven sibling (`RefreshQueryDrivenAsync`) catches and logs.
- **Why it bites:** If fzf is missing or the spawn fails, the exception is unobserved (net472 swallows unobserved task exceptions) and the user silently gets stale results with no diagnostic.
- **Fix:** Wrap the awaits in try/catch and log via `TelescopeLog` ("filter failed: ..."), or attach a continuation that logs the fault.

### M9–M11 — preview/motion correctness (carried from R1–R5, re-verified)
- **M9** `Telescope/SyntaxHighlighter.cs:198` — `ReadQuoted` does `i += 2` after a quote, overshooting past the closing quote on `""`/escaped sequences → mis-tokenized previews with no diagnostic.
- **M10** `Telescope/TextMotionNavigator.cs:120` — `Up()` uses `LastIndexOf` with a negative `startIndex` when the caret is on line 0 → `ArgumentOutOfRangeException` or wrong caret.
- **M11** `Telescope/PreviewRenderer.cs:141` — `CaretToPointer` returns null on a blank line → caret placement silently no-ops.
- **Why they bite:** All three are on the e2e-asserted preview path (`preview caret=... line=...`); each is a silent wrong-caret failure that the harness can only catch as a flaky assertion.
- **Fix:** M9: advance by the actual quote length; M10: clamp `startIndex` to 0; M11: fall back to the paragraph start on a blank line.

### M12 — one bad frame aborts navigation
- **Where:** `MyExtension/CardinalMovment/WindowMatrix.cs:98,106`; `WindowAdapter.cs:120-127`.
- **What's wrong:** `AutoHides()` is called outside the try/catch, and the unguarded `(IVsWindowFrame4)_frame` cast in `RefreshRect` throws `InvalidCastException` for a non-conforming frame — caught by `NavigateInDirection`'s catch → the whole navigation silently no-ops (ref F7).
- **Why it bites:** One unpaired/odd frame (a tool window that doesn't implement `IVsWindowFrame4`) kills navigation in that direction with only a Debug log.
- **Fix:** Use `_frame as IVsWindowFrame4` + null check (or cache the cast once in the ctor); wrap `AutoHides()` in the same per-frame try/catch.

### M13 — `Classify()` mislabels issue kinds
- **Where:** `Telescope/CodeIssuesFinder.cs:186`.
- **What's wrong:** `Classify` guesses kind by `description.ToLowerInvariant().IndexOf("error"/"warning")` instead of the Error List's real severity (`ErrorItem.Severity`). A warning whose message contains "error" (or an error that doesn't) is shown as the wrong kind. The logic is unreachable by the test seam (`callers_of("Classify")` = only `CollectErrorList`, which the `_testFileSource` path bypasses).
- **Why it bites:** Wrong `[ERR]`/`[WARN]` rows with no way to correct, and the bug can't be unit-tested.
- **Fix:** Use `item.Severity`; extract a pure `ErrorItem→CodeIssue` mapper (or make `Classify` internal) so the Error List path is testable without VS.

### M14 — `BuildForest` nested-folder expansion gap
- **Where:** `MyExtension/ToolWindows/SolutionExplorerController.cs:295`.
- **What's wrong:** The forest builder's recursion (which kinds recurse, `.cs` filter, `FileCount` indexing) is DTE-coupled and untested — tests only construct `new SolutionExplorerController(() => null!)`, so the forest logic has zero coverage.
- **Why it bites:** A regression in folder recursion / `.cs` filtering is invisible until live e2e.
- **Fix:** Extract a pure walker over `(kind, name, path, children)` tuples mirroring `HierarchyResolver` so the build side is unit-testable.

### M15 — `action()` with no try/catch in the hook path
- **Where:** `MyExtension/LeaderSequenceMatcher.cs:63`.
- **What's wrong:** The matched binding's `Action` delegate is invoked with no try/catch; a handler exception escapes into the hook callback.
- **Why it bites:** A binding handler that throws (e.g. `KeyNotFoundException` from a bad finder name — see M30) crashes the low-level hook callback; Windows may remove the hook.
- **Fix:** Wrap `action()` in try/catch that logs `[NeoVisual] leader-binding failed: ...`.

### M16 — `FocusGuard` text-input reverse-leak
- **Where:** `MyExtension/ToolWindows/FocusGuard.cs:26`.
- **What's wrong:** `isTextInputSurface` is never vetoed — a text-input surface (Command Window) is treated as owning the keyboard even when the editor holds focus.
- **Why it bites:** The veto logic is the guard against the `ljoljoljo` leak class; a mis-wired text-input surface re-opens the leak for those windows.
- **Fix:** Re-derive the veto so a text-input surface only owns the keyboard when it actually has focus (the `EditorFocusedVeto` composition in `InputHandler` must stay consistent with the guard's truth table).

### M17 — VsVim mode-tracking staleness
- **Where:** `MyExtension/VimModeTracker.cs:189,209,483-488`.
- **What's wrong:** `_cachedTyping = false` set unconditionally on `LostAggregateFocus` (out-of-order focus events); `_resolved` latches on first failure; `(int)value` cast on a null out-param can throw (ref F5).
- **Why it bites:** A VsVim update or a focus race silently flips leader-key behavior (Space types a literal space instead of starting a leader) with zero unit coverage.
- **Fix:** Route all `_cachedTyping` writes through one method that owns the field + the `vim-mode=` log; guard the cast; don't latch `_resolved` permanently.

### M18 — logging races + swallowed pane failures
- **Where:** `Telescope/NeoVisualLog.cs:109,132-148`.
- **What's wrong:** The shared writer is touched from multiple paths; `WriteToPane` swallows pane-creation/write failures with an empty catch.
- **Why it bites:** If the `NeoVisual` Output pane can't be created, the pane silently never appears and the harness's pane-based diagnostics are missing with no log line explaining why.
- **Fix:** Log a one-time fallback line on first pane failure; serialize the writer access.

### M19 — no-timeout COM call in dte-command
- **Where:** `tools/dte-command.ps1:92`.
- **What's wrong:** A COM call (e.g. `Debug.Start`) has no timeout.
- **Why it bites:** A hung COM call blocks the harness forever with no budget and no diagnostic.
- **Fix:** Enforce a timeout (the `Assert-Budget` pattern from `iterate-telescope.ps1`).

### M20 — test-suite hermeticity holes
- **Where:** `tests/Telescope.Tests/Program.cs:149,286,774`; `tests/NeoVisual.Tests/Program.cs:78`.
- **What's wrong:** (a) LogFileWriter tests are order-dependent on the once-per-process `_clearedThisProcess` flag; (b) `Run_FzfFilter_FilterMatchesPrefix` silently passes when fzf is absent; (c) `Run_Keybinding_DefaultFileHasTelescopeAndNav` reads the user's real `%APPDATA%\MyExtension\keybindings.json` (ref F43/F45).
- **Why it bites:** A user with a custom config gets a spurious failure on a correct build; a machine without fzf reports green while the filter path is unverified; a reordered suite breaks confusingly.
- **Fix:** Load the embedded defaults only (hermetic seam); fail loudly (or route through the runner's accounting) when fzf is absent; make `Clear()` idempotent per-path.

### M21–M30 — duplication cluster (merge-systems round)
- **M21** `KeyToString` duplicated verbatim (`InputHandler.cs:469` vs `LeaderSequenceMatcher.cs:97`) — a new printable-key mapping in one copy silently diverges, breaking the `keybindings.json` name↔`Keys` round-trip. **Fix:** one shared `KeyNames.ToString(Keys)`.
- **M22** 3rd DTE walker: `FileFinder.CollectProjectFiles` (`FileFinder.cs:103`) and `FindFirstSourceFileInItems` (`MyExtensionPackage.cs:285`) duplicate `ProjectFiles.Enumerate` (ref F13). **Fix:** `FileFinder.GatherHits` calls `ProjectFiles.Enumerate(dte)` + maps to `FileHit`; the package reuses it for the auto-open path.
- **M23** `OpenReference`/`OpenImplementation` byte-identical (`MyExtensionPackage.cs:442-466`). **Fix:** one `OpenHitAtLine(IFileLocation)` (both hit models implement `IFileLocation`).
- **M24** Vim key→motion dispatch triplicated (`TryPromptMotion`/`HandlePreviewKey`/`TextMotionHelper.MapMotion`) — already drifted (`$` Shift-gated in prompt, bare in preview). **Fix:** one shared `TryDispatch(Keys, navigator)`; each surface keeps only its apply-caret step.
- **M25** Block caret rendered in 3 places (`BlockCaretAdornment` / `ApplyPromptCaretStyle` / `TextMotionHelper`). **Fix:** share the block-caret geometry/brush helper.
- **M26** Focus-keeper `DispatcherTimer` idiom duplicated (`SolutionExplorerController.cs:114,241`) — already diverged (one injects Escapes, one doesn't). **Fix:** one `FocusKeeper.Run(interval, durationMs, reassert)`.
- **M27** Log pipeline 4 overlapping entry points (`NeoVisualLog.Debug` alias of `Log`; `LogFileWriter.Write`/`WriteDebug` byte-identical; `NeoVisualTraceListener.Write`/`WriteLine` byte-identical). **Fix:** one `WriteTo(ref writer, path, message)` core; delete the `Debug` alias (30 call sites → `Log`).
- **M28** Harness duplication: 5 `Open-Telescope*` copies, 9× SE-toggle loop, 3 wait helpers, 2× seed canonical content. **Fix:** one `Open-TelescopeFinder -Key -Finder`, one `Ensure-SolutionExplorerOpen`, one `Wait-LogLine -FromIndex -PollMs`, one seed-content map.
- **M29** Test scaffolding copy-pasted: temp-dir 23×, LogPath save/restore 8×, duplicate MapMotion groups. **Fix:** shared `TempDir` IDisposable + `WithLogPath` helper; one canonical MapMotion test.
- **M30** Action-name→finder-name mapping split across `Actions.Registry` and `TelescopeLauncher.FinderNames` — a typo throws `KeyNotFoundException` in the hook path. **Fix:** single source of truth (one map, or derive `FinderNames` from `Actions.Registry`).

### M31–M34 — testability seams (ease-of-use round)
- **M31** Simple-shortcut matching buried in the VS-coupled `HandleKey` — untestable without AsyncPackage+MEF+WindowManager. **Fix:** extract a dependency-free `SimpleShortcutMatcher` (mirror `LeaderSequenceMatcher`).
- **M32** Mode classifier + VsVim reflection interop not behind a seam — untestable without live VS + VsVim. **Fix:** extract a pure `VimModeClassifier.Classify(int? mode)` and a narrow `IVimModeSource` seam with a fake for tests.
- **M33** `InitializeAsync` (blast radius 85) wraps ~10 steps in ONE catch-all — a failure names no step and partial state is invisible. **Fix:** per-step try/catch with distinct diagnostics.
- **M34** Overlay focus-target state machine buried in the WPF `OnPreviewKeyDown` override (blast radius 51). **Fix:** move focus-target state + Ctrl+H/L/Escape transitions into `OverlayKeyHandler` (or a pure `FocusTargetModel`).

### M35–M37 — log pipeline + harness robustness
- **M35** `LogFileWriter.Write`/`WriteDebug` swallow every exception — the log pipeline is undiagnosable when it fails (harness times out with zero indication). **Fix:** track a `WriteFailureCount` (or one-time fallback) exposed to tests/harness.
- **M36** `iterate-telescope.ps1:112` blanket `Stop-Process -Force` kills the user's unrelated VS instances (ref F42); `:104` USER-scope env mutation persists across sessions. **Fix:** scope kills to spawned PIDs + `Save-AllDocuments` (the `test-e2e.ps1` M-M5 pattern); use Process scope.
- **M37** `test-e2e.ps1:73` `$TimeoutSec = 300` declared but never enforced. **Fix:** wire it into an overall `Assert-Budget` or delete it.

### M38–M42 — naming/conventions (naming round)
- **M38** `WindowManager` in the GLOBAL namespace (no `namespace` block) while every sibling uses `namespace MyExtension`. **Fix:** wrap in `namespace MyExtension`, drop the self-`using`.
- **M39** `Direction.Axis()` returns the PERPENDICULAR axis (Up→X) — the opposite of what the name implies. **Fix:** rename to `PerpendicularAxis()`/`OverlapAxis()`.
- **M40** `NavigateInDirection(char)` takes magic `'U'/'D'/'L'/'R'` instead of the existing `Direction` enum. **Fix:** change the signature to `NavigateInDirection(Direction)`; delete the char constants + `ToDirection`.
- **M41** `GrepFinder.GatherHits()` stub always returns empty — the name lies (real logic is in `GetCandidates(string)`). **Fix:** delete the stub (or `throw NotSupportedException`) and rename the query path to make the static-vs-query-driven split explicit.
- **M42** Test names lie: "StartInInsert/StartInNormal" only classify `IsTextInputType`; `TextInput_*` MapMotion tests duplicate `TextMotionEngine_*`. **Fix:** rename to what they assert; consolidate the MapMotion groups.

### M43 — flush timer fires forever
- **Where:** `Telescope/LogFileWriter.cs:206`; `Telescope/NeoVisualLog.cs:82`.
- **What's wrong:** The 200ms flush timer fires forever; `NeoVisualLog.Close` is never called in production (the R7 "0 callers" was a proxy trap — `MyExtensionPackage.Dispose` does call it, but only on package shutdown, so the timer runs for the whole VS session).
- **Why it bites:** 5 wakeups/sec for the entire session, each taking the `Sync` lock and flushing two StreamWriters even when idle.
- **Fix:** Use a one-shot `Change(200, Timeout.Infinite)` that re-arms only on a write, or dispose the timer on `Clear`.

## Recommendations (ordered by effort/impact)

1. **Fix the 3 criticals first** (CR1 controller overwrite, CR2 runner false-PASS, CR3 deferred ShowDialog) — each is a small, high-blast-radius fix gated on the existing e2e scenarios. CR1 and CR2 are consolidation-introduced regressions; CR3 is a latent race.
2. **Restore the hook hot-path contract** (M1 per-key log, M3 `File.Exists` per key, M4 `IsTextInputType` ×3, m1 `BuildSimpleKey` alloc) — one focused pass on `GlobalKeyboardHook`/`InputHandler`/`WindowManager` to make the pre-filter genuinely cheap again.
3. **Amortize the finder path** (M5 GrepFinder cache, M6 fzf timeout/fallback, M7 preview caret index, M8 filter try/catch) — the four Telescope UI-stall findings; each is a localized change behind an existing seam.
4. **Merge the duplication clusters** (M21–M30) — the merge-systems round produced exact merge targets with named blast radius; start with the pure-logic merges (KeyToString, OpenHitAtLine, LogFileWriter core, vim-motion dispatch) that have hermetic test seams.
5. **Extract the missing testability seams** (M31 shortcut matcher, M32 VimModeClassifier + IVimModeSource, M34 focus-target model) — the repo's own `OverlayKeyHandler`/`LeaderSequenceMatcher` pattern; each makes a currently e2e-only behavior unit-testable.
6. **Harness hardening** (M36 scoped kills + Process-scope env, M37 enforce `$TimeoutSec`, M28 helper consolidation, m26/m28 cache-index discipline) — prevents data loss and false PASS/FAIL.
7. **Naming/convention sweep** (M38–M42, n1–n12) — low-risk, mostly mechanical; do after the behavior fixes.

## Filed into progress.md

Pending user approval — see the `question` prompt. Candidate findings for the build-hub backlog: CR1, CR2, CR3 (critical); M1, M2, M3, M4, M5, M6, M7, M8, M9, M10, M11, M12, M13, M14, M15, M16, M17, M18, M19, M20, M21, M22, M23, M24, M25, M26, M27, M28, M29, M30, M31, M32, M33, M34, M35, M36, M37, M38, M39, M40, M41, M42, M43 (major). Cross-references to `docs/architecture-review.md`: F2→M1, F7→M12, F8/F9→M6, F13→M22, F26→CR3, F27→M8, F28→m23, F38→CR2, F43→M20, F45→M20. F15/F22 were fixed by the consolidation and are NOT re-reported.
