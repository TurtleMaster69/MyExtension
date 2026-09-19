# MyExtension — Architecture Review

- **Date:** 2026-09-19
- **Scope:** full repo — `MyExtension/` (core + `CardinalMovment/` + `ToolWindows/`), `Telescope/`, `tests/`, `tools/`, cross-cutting.
- **Method:** 5 parallel `arch-auditor` subagents (slice A core, B CardinalMovment, C ToolWindows, D Telescope, E tests+tools) + hub-conducted slice F (cross-cutting). Every finding below was verified against current code by the hub or an auditor; claims contradicted by the code were dropped. No files under `MyExtension/`, `Telescope/`, `tests/`, `tools/` were modified.

## Summary

**46 findings (of 46 total): 1 critical, 15 major, 30 minor/nit.** The architecture is healthy at the seams the repo already invested in — pure state machines (`OverlayKeyHandler`, `TextMotionNavigator`), the UI-thread hook discipline, net472 compliance, and the diagnostics-as-contract logging are all in good shape (verified clean: no `IReadOnlySet<T>`/modern-BCL usage; `CardinalNavigation` namespace stays isolated in the typo'd `CardinalMovment` folder; the two-file log system is consistent). The sharp edges are (1) the **Enter-storm re-injection loop**, a critical re-entrancy bug that keeps 3 live E2E scenarios red and can fire ~30 injections per second for any user pressing Enter/o in Solution Explorer; (2) a **hook hot path that violates its own documented "cheap pre-filter" contract** (per-key file logging + `File.AppendAllText` on the UI thread); and (3) a **duplication cluster around vim-motion dispatch and caret rendering** that has already drifted once (black vs white block caret). The Telescope finders roadmap (references/grep/fzf/implementation) runs directly into four scalability gaps: per-keystroke fzf spawning with no debounce, preview reload/re-tokenize per selection, a finder seam that hardcodes payload types in the overlay code-behind, and per-open DTE re-enumeration with no cache.

## Findings table

| id | severity | file:line | problem |
|----|----------|-----------|---------|
| F1 | critical | `MyExtension/ToolWindows/SolutionExplorerController.cs:98` | Injected Return re-enters the hook → infinite re-injection storm (Enter-storm); no guard anywhere in the chain |
| F2 | major | `MyExtension/GlobalKeyboardHook.cs:116` | Per-key `[Hook]` log + `File.AppendAllText`-per-write on the UI thread violate the documented cheap pre-filter contract |
| F3 | major | `MyExtension/ToolWindows/TextMotionHelper.cs:68` | Vim key→motion dispatch duplicated 4× (two verbatim copies + two overlay copies); one log contract each |
| F4 | major | `MyExtension/ToolWindows/TextInputToolWindowController.cs:330` | Block caret implemented 3 places; text-input controller paints BLACK vs white elsewhere |
| F5 | major | `MyExtension/VimModeTracker.cs:159` | Focus loss leaves stale vim buffer subscribed; late `SwitchedMode` flips `_cachedTyping`; `_resolved` latches on failure |
| F6 | major | `MyExtension/CardinalMovment/WindowMatrix.cs:429` | Filter pipeline COM-bound with no pure rect seam and no outcome diagnostic → untestable, e2e-blind |
| F7 | major | `MyExtension/CardinalMovment/WindowControlAdapter.cs:39` | One unpaired frame throws E_FAIL → whole navigation degrades to no-op |
| F8 | major | `Telescope/FzfFilter.cs:102` | fzf spawned per keystroke; full candidate list written synchronously on UI thread; no debounce |
| F9 | major | `Telescope/FzfFilter.cs:133` | fzf missing/crash silently returns full list; `IsAvailable()` has zero production callers |
| F10 | major | `Telescope/TelescopeOverlay.cs:402` | Preview file re-read + re-tokenized + caret reset on every selection/filter change |
| F11 | major | `Telescope/TelescopeFinder.cs:14` | Finder seam doesn't scale: preview/line-jump hardcoded by payload type in overlay code-behind; sync `GetCandidates` on UI thread |
| F12 | major | `Telescope/TelescopeOverlay.cs:357` | Display-keyed payload lookup loses same-named duplicates → null-payload entries that silently do nothing |
| F13 | major | `Telescope/FileFinder.cs:116` | FileFinder re-implements the shared `ProjectFiles` DTE traversal (second walker) |
| F14 | major | `MyExtension/PopupNavigation.cs:49` | Ctrl+N/P hijacked in every editor — arrow injected with no popup-active check; native VS shortcuts dead |
| F15 | major | `Telescope/CodeIssuesFinder.cs:71` | Per-open DTE re-enumeration + `ReadAllLines` of every project file on UI thread; no cache |
| F16 | major | `tools/test-e2e.ps1:806` | 4 known-bug assertions still red (prompt-motions `e`, `key=Enter`→`Return`, issues `count=1`); no Enter-storm fail-fast guard |
| F17 | minor | `MyExtension/InputHandler.cs:499` | `GetWindowRect` P/Invoke + `OpenTelescope` duplicated with `MyExtensionPackage.cs:417/401` |
| F18 | minor | `MyExtension/CardinalMovment/WindowMatrix.cs:245` | Dead/duplicated code: `RemoveWindowsNotAdjacent`, `ActivateWindow`, unused ctor/field, `DistinctBy` no production caller, Min-exception-as-control-flow |
| F19 | minor | `MyExtension/CardinalMovment/WindowMatrix.cs:161` | Sort comparer allocates rects per comparison; distance computed twice per candidate |
| F20 | minor | `MyExtension/CardinalMovment/WindowMatrix.cs:202` | Missing `ThrowIfNotOnUIThread()` in several predicates + `WindowManager` ctor + `SolutionExplorerController.ExecuteCommand` |
| F21 | minor | `MyExtension/CardinalMovment/WindowMatrix.cs:213` | Edge/emptiness rect math inline ~15 sites; `RectCoordinate` is a bare field holder |
| F22 | minor | `MyExtension/InputHandler.cs:210` | Leader state machine + `BuildSimpleKey` not extracted → core routing untestable offline |
| F23 | minor | `MyExtension/GlobalKeyboardHook.cs:175` | Ctrl-key pre-filter cases dead; AGENTS.md "completion swallow" claim not implemented |
| F24 | minor | `MyExtension/GlobalKeyboardHook.cs:170` | h/j/k/l/i unconditionally interesting → full `HandleKey` per editor keystroke |
| F25 | minor | `Telescope/TelescopeOverlay.cs:467` | Prompt caret always placed at end; 'I' start-insert path dead |
| F26 | minor | `Telescope/TelescopeOverlay.cs:292` | `IsOpen=true` before deferred `ShowDialog` — keys in gap reach editor; open-then-close throws on dispatcher |
| F27 | minor | `Telescope/TelescopeOverlay.cs:339` | Fire-and-forget `FilterAndUpdateAsync` with no try/catch |
| F28 | minor | `Telescope/LogFileWriter.cs:42` | `Clear()` once-per-process vs `ShowOverlay` Clear-on-open — logs accumulate across opens |
| F29 | minor | `Telescope/TelescopeController.cs:56` | Ad-hoc `Debug.WriteLine` bypasses the shared `NeoVisualLog` path (also `TelescopeOverlay.cs:857`, `VimModeTracker.cs:224`) |
| F30 | minor | `MyExtension/ToolWindows/SolutionExplorerController.cs:78` | Double visual-tree walk per routed key |
| F31 | minor | `MyExtension/ToolWindows/TextInputToolWindowController.cs:312` | `BlockCaretAdornment.Active` never cleared on tool-window focus loss |
| F32 | minor | `MyExtension/ToolWindows/TextInputToolWindowController.cs:147` | IWpfTextView read failure swallowed silently (no diagnostic) |
| F33 | minor | `MyExtension/ToolWindows/GeneralToolWindowController.cs:34` | `IsTextInputType` branch dead — text-input types always route to `TextInputToolWindowController` |
| F34 | minor | `MyExtension/ToolWindows/GeneralToolWindowController.cs:66` | `KeyToArrowVk` + log emitter duplicated with `SolutionExplorerController.cs:188` |
| F35 | minor | `MyExtension/ToolWindows/TextMotionHelper.cs:169` | `GetAsyncKeyState` P/Invoke declared twice |
| F36 | minor | `tests/Telescope.Tests/Program.cs:26` | Test runner + `Assert` copy-pasted between the two test projects |
| F37 | minor | `tools/iterate-telescope.ps1:6` | Stale doc comment; divergent overlay-close; helpers triplicated with `test-e2e.ps1`/`dte-command.ps1` |
| F38 | minor | `tools/test-e2e.ps1:1092` | Runner ignores a scenario's `$false` return → silent false PASS |
| F39 | minor | `tools/test-e2e.ps1:201` | `Wait-NewLogLine` re-reads whole log per poll → O(n²) I/O on long runs |
| F40 | minor | `tools/test-e2e.ps1:1095` | Failure output has no log tail / step context |
| F41 | minor | `tools/test-e2e.ps1:92` | `Send-Text` char→VK mapping wrong for punctuation (`!` → VK_PRIOR/PageUp) |
| F42 | minor | `tools/test-e2e.ps1:1105` | On failure kills ALL `devenv` processes on the machine |
| F43 | minor | `tests/Telescope.Tests/Program.cs:150` | fzf filter test silently PASSES when fzf is not on PATH |
| F44 | minor | `tools/dte-command.ps1:13` | Hardcoded VS PublicAssemblies paths, no vswhere fallback |
| F45 | minor | `tests/Telescope.Tests/Program.cs:114` | LogFileWriter test order-dependent on static `_clearedThisProcess`; log-prefix constants not centralized |
| F46 | minor | `MyExtension/MyExtension.csproj:31` | Host depends on the "library" for core infra (`NeoVisualLog`, `TelescopeController`); split is not a clean pure/host boundary |

## Detailed findings

### F1 (critical) — Enter-storm re-injection loop
- **Where:** `MyExtension/ToolWindows/SolutionExplorerController.cs:98` (Enter→`OpenSelected`), `:137` (`KeyInjection.Press(VK_RETURN)`); `MyExtension/InputHandler.cs:283` (`controller.ActionKeys.Contains(key)` re-routes); `MyExtension/KeyInjection.cs:17-19` (stale doc claiming "we only ever inject arrows" — VK_RETURN/VK_F2 added at :35-36).
- **What:** Physical Enter (or `o`) in Solution Explorer normal mode → `TryMove(Enter)` → `OpenSelected()` → injected Return → the injected key-down re-enters `HookCallback` (`IsInteresting` true because Enter is an action key and `HasToolWindowActionKeys` covers any key in normal mode) → `HandleKey` → `TryMove(Enter)` again → another injection. Unbounded. Verified by two independent auditors and by hub code reading; `docs/progress.md` already records "~30x in ~100ms".
- **Why it bites:** Live E2E `neovisual-explorer-open` / `neovisual-explorer-open-o` stay red; every real user pressing Enter or `o` in Solution Explorer hits a recursion that Windows eventually kills by silently removing the low-level hook — after which the extension is dead until VS restarts, with no diagnostic.
- **Fix:** In `HookCallback`, read `KBDLLHOOKSTRUCT.flags` (lParam + 8) and bail on `LLKHF_INJECTED` (0x10) so injected keys pass through to the tree instead of re-entering routing — this also future-proofs F2/arrow injections. Alternative: in-flight guard in `KeyInjection.Press` (a plain bool is racy — the injected event is delivered after `Press` returns). Then add a harness fail-fast: assert at most one `solution-explorer open` line post-baseline (see F16).

### F2 (major) — Hook hot path violates the documented cheap pre-filter contract
- **Where:** `MyExtension/GlobalKeyboardHook.cs:116`; `Telescope/LogFileWriter.cs:95`.
- **What:** Every "interesting" key (all Ctrl chords, every h/j/k/l/i, every leader key) calls `Telescope.NeoVisualLog.Log`, which per line does `LogFileWriter.Append` → `File.AppendAllText` (open/write/close under a lock), `Debug.WriteLine`, and `OutputStringThreadSafe` — all inline on the UI thread inside the low-level hook callback. The file's own comment at :218 says "NOT for the per-key path" and the class doc promises "the common path costs a few Win32 calls only". The e2e harness never asserts on `[Hook]` lines.
- **Why it bites:** Directly contradicts `AGENTS.md` ("NEVER add per-key logging back to the callback"). Low-level hook callbacks that block too long are silently removed by Windows — the extension can be disabled by its own logging under heavy typing, with no log line explaining it (the log that would explain it is the thing that got it killed).
- **Fix:** Delete line 116 (keep install/uninstall lines), or gate behind an env/registry flag. Keep `IsInteresting` allocation-free.

### F3 (major) — Vim key→motion dispatch duplicated 4×
- **Where:** `MyExtension/ToolWindows/TextMotionHelper.cs:68` vs `MyExtension/ToolWindows/TextInputToolWindowController.cs:181` (verbatim: same 8-case `TextMotionNavigator` switch, same insert side effect, same `text-motion`/`textinput-enter-input` log contract, `MotionName` duplicated at :125/:223); `Telescope/TelescopeOverlay.cs:561` (`TryPromptMotion` re-implements the dispatch + builds a new navigator per keypress) vs `:599` (`HandlePreviewKey` third dispatch).
- **Why it bites:** The e2e harness asserts `text-motion`/`prompt-motion` lines from all these surfaces. Format drift in one copy silently fails the suite; the block-brush copy already drifted once (see F4). Per-key navigator allocation in the prompt path.
- **Fix:** One shared `Key→(TextMotionNavigator motion)` dispatch helper + one log emitter, used by the overlay prompt, overlay preview, and both tool-window paths; keep the pure `TextMotionNavigator` as the only motion math.

### F4 (major) — Block caret triplicated, one copy drifted to black
- **Where:** `MyExtension/ToolWindows/TextInputToolWindowController.cs:330` (`CreateBlockBrush` paints `Brushes.Black`); `MyExtension/ToolWindows/TextMotionHelper.cs:163` and `MyExtension/BlockCaretAdornment.cs` paint white; `Telescope/TelescopeOverlay.cs:484` (`ApplyPromptCaretStyle`) is a fourth caret implementation.
- **Why it bites:** Normal-mode WPF TextBoxes in Command Window/FindReplace/Immediate draw a **black** block caret — invisible on the default dark VS theme and inconsistent with the Solution Explorer search box (white). No e2e assertion covers brush color, so the harness can't catch it. Caret rendering exists in 4 places with 3 visual contracts; a VsVim-theme change multiplies the drift.
- **Fix:** Single `ApplyCaretStyle(box, isNormalMode)` on `TextMotionHelper` (or a pure caret-state class); delete `CreateBlockBrush`. Also fold in F31 (adornment never deactivated on focus loss).

### F5 (major) — VimModeTracker: stale subscription + latched failure → silent mode-detection degradation
- **Where:** `MyExtension/VimModeTracker.cs:159-165` (`OnViewLostFocus` only clears `_cachedTyping`, never unsubscribes `_currentBuffer`/`_currentTextBuffer`); `:152-155` (no-buffer path leaves old subscription); `:307-316` (`OnSwitchedMode` trusts `ReferenceEquals(sender, _currentTextBuffer)`); `:457` (`_resolved` latched before the MEF resolution try); reflection failures only `Debug.WriteLine` (:224/:234/:254/:301).
- **Why it bites:** A stale subscribed buffer's late `SwitchedMode` event flips `_cachedTyping=true` while focus is elsewhere (tool window / non-vim surface) → Space starts a leader instead of typing, or vice versa — silent, no diagnostic. And because `_resolved` latches on the first failure, a too-early resolution (tracker is an editor listener that can be created before package init) permanently disables VsVim detection for the process; a VsVim update that renames a member kills the integration with the failure only in debug output, invisible to the harness.
- **Fix:** Unsubscribe + null refs in `OnViewLostFocus` and the no-buffer path; don't latch `_resolved` on failure (retry); route failure diagnostics through `NeoVisualLog.Log`.

### F6 (major) — WindowMatrix filter pipeline untestable and e2e-blind
- **Where:** `MyExtension/CardinalMovment/WindowMatrix.cs:429` (pipeline operates directly on COM-bound `WindowControlAdapter`); `MyExtension/InputHandler.cs:420` (only logs `navigate direction=` before construction).
- **Why it bites:** The 6-step filter is the core navigation algorithm but has no offline tests, and e2e cannot distinguish a no-op navigation from a successful selection (no outcome log). A future chained-movement/jump extension or a filter-order bug is invisible to the 25-scenario suite until a user complains.
- **Fix:** Extract `ReduceWindows(IReadOnlyList<RectCoordinate> candidates, RectCoordinate active, char direction)` as a pure state machine (the `OverlayKeyHandler` pattern) and delegate from `ReduceWindowsAndSelectActive`; log `[NeoVisual] navigate activated=...` / `no-op`.

### F7 (major) — One unpaired frame kills ALL cardinal navigation
- **Where:** `MyExtension/CardinalMovment/WindowControlAdapter.cs:39` (ctor throws E_FAIL when `VsShellUtilities.GetWindowObject` returns null), called at :151 during enumeration; `WindowMatrix.cs:87` catches and degrades the whole navigation to no-op.
- **Why it bites:** A single frame whose DTE window object can't be resolved (window list mid-change, a third-party tool window) silently disables navigation in all four directions for that invocation. `WindowMatrix.cs:83-84` claims per-window degradation that doesn't exist.
- **Fix:** Skip non-null pairings — `yield` only frames that pair successfully, instead of throwing.

### F8 (major) — fzf spawned per keystroke with synchronous UI-thread pipe write
- **Where:** `Telescope/FzfFilter.cs:102-115`; caller `Telescope/TelescopeOverlay.cs:339` (per `OnPromptTextChanged`).
- **Why it bites:** Every keystroke spawns a process, `string.Join`s all candidates, and synchronously writes 4–64 KB into the pipe on the UI thread before the first await. The roadmap's grep/references finders will feed thousands of candidates → per-keystroke UI stall, and the pending-show race compounds it.
- **Fix:** `Task.Run` around the process I/O (the class doc already promises "can be invoked from a background task"), plus a debounce in `RefreshResults`. Documented alternative: fzf `--listen` mode or an in-process matcher.

### F9 (major) — fzf missing/crash is silent; `IsAvailable()` unused
- **Where:** `Telescope/FzfFilter.cs:133-136` (catch → return full list), `:51-70` (`IsAvailable()` — verified zero production callers, only `tests/Telescope.Tests/Program.cs:152`).
- **Why it bites:** A machine without fzf makes telescope-search silently non-functional — e2e fails with no way to distinguish "fzf missing" from a filter bug, and users get no hint. The doc promise ("degrade gracefully … + a warning") doesn't exist.
- **Fix:** Call `IsAvailable()` once at controller construction and log `[Telescope] fzf unavailable — filtering disabled`; log the fallback in the catch too.

### F10 (major) — Preview reload + re-tokenize per selection change
- **Where:** `Telescope/TelescopeOverlay.cs:402` (`LoadPreviewForSelection` on every `RenderResults` — every j/k move and every filter keystroke: `File.ReadAllText`, `SyntaxHighlighter.Segment`, full `FlowDocument` rebuild, caret reset to top).
- **Why it bites:** Real-sized files freeze the overlay per keystroke; the user's preview scroll/caret is destroyed just by moving list selection. e2e passes only because scratch files are tiny.
- **Fix:** Cache the last preview payload (path + content hash + segments + navigator text) and skip rebuild when unchanged.

### F11 (major) — Finder seam doesn't scale to the roadmap
- **Where:** `Telescope/TelescopeFinder.cs:14` (IFinder exposes only `Name`/`GetCandidates`/`OnSelected`); `Telescope/TelescopeOverlay.cs:410-445` (preview rendering hardcodes `payload is CodeIssue` / `payload is string`); `GetCandidates` synchronous on the UI thread.
- **Why it bites:** Each of the 4 pending finders (references/grep/implementation) requires editing the overlay code-behind with a new payload type + preview branch + line-jump, and references/grep (long-running) freeze the UI at open.
- **Fix:** Add a preview/open seam to IFinder (e.g. `Preview(entry) → {text, line?}`) and make candidate gathering async/lazy.

### F12 (major) — Display-keyed payload lookup loses same-named duplicates
- **Where:** `Telescope/TelescopeOverlay.cs:357` (`GroupBy(Display).First()` + `new FinderEntry(m)` null-payload fallback).
- **Why it bites:** Two same-named files in different folders (App.xaml/MainWindow.xaml in multi-project solutions) both appear as candidates but only the first is selectable — the second silently does nothing. The em-dash UTF-8 fix is a band-aid over the same lossy display-keyed design.
- **Fix:** Carry the payload through filtering (filter display lines, map matched lines back by stable ordinal/id, not display text).

### F13 (major) — FileFinder re-implements the shared DTE walker
- **Where:** `Telescope/FileFinder.cs:116-190` vs `Telescope/ProjectFiles.cs:31-103` (same solution-folder GUID, same `FullPath` read, same `File.Exists`/seen dedup — near-verbatim).
- **Why it bites:** AGENTS.md documents `ProjectFiles` as the shared enumeration "used by finders AND the issues finder", but only `CodeIssuesFinder` uses it. A bug fix or new project-kind handling in one copy silently drifts from the other.
- **Fix:** `GetCandidates` maps `ProjectFiles.Enumerate(dte)` to `FinderEntry`; delete the private copies.

### F14 (major) — Ctrl+N/Ctrl+P hijacked in every editor
- **Where:** `MyExtension/PopupNavigation.cs:49` (unconditional arrow injection when not a tool window); routing at `MyExtension/InputHandler.cs:241-244`.
- **Why it bites:** In any code editor, Ctrl+N/Ctrl+P always inject Down/Up arrows (caret moves) — VS's native Ctrl+N (New Project) and Ctrl+P (print) can never fire, even with no completion popup open. The old `IsCompletionActive`/`IsInEditor` gating is gone. Silent, user-visible, no diagnostic, no e2e scenario.
- **Fix:** Re-add a popup-active check (MEF `ICompletionBroker.IsCompletionActive`) before injecting; pass through when no popup is open.

### F15 (major) — CodeIssuesFinder: per-open DTE re-enumeration + full file scans on UI thread
- **Where:** `Telescope/CodeIssuesFinder.cs:71` (+ `CollectTodos` :139-157 `File.ReadAllLines` per project file + whole Error List read), `Telescope/ProjectFiles.cs` enumeration on every open; `FileFinder` re-enumerates again in the same session.
- **Why it bites:** Opening the Issues finder on a real solution is a multi-second UI freeze; consecutive Files/Issues opens duplicate the DTE walk with no shared cache.
- **Fix:** Cache `ProjectFiles.Enumerate` per session in `TelescopeController` (invalidate on solution change); scan TODO markers lazily/async.

### F16 (major) — Harness still asserts the 4 known-bug expectations
- **Where:** `tools/test-e2e.ps1:806` (`prompt-motion key=E caret=5` — actual is 4, and the following `w` taps are off by one), `:910` (`key=Enter` — actual `key=Return`), `:764` (`results count=1` — Error List accumulates session warnings), `:441`/`:530` (no Enter-storm fail-fast guard; loops only assert a single occurrence and can pass while storming, failing via ~32 s timeout).
- **Why it bites:** 4 of 25 scenarios are known-red; `docs/progress.md` already tracks these. The Enter-storm scenario is the worst — it fails slowly with no diagnostic.
- **Fix:** Apply the corrected expectations from `docs/progress.md` (e→4, w→5/8/12, `key=Return mode=normal handled=True`, `results count=\d+`); add a post-baseline "at most one `solution-explorer open`" assertion and `Assert-VsFocused` inside the walk loop.

### F17–F46 (minor/nit) — see the findings table; highlights:

- **F18/F19/F21** — `WindowMatrix` carries dead code (`RemoveWindowsNotAdjacent`, private `ActivateWindow`, unused ctor/field), redundant re-filter passes (:294 re-runs wrong-direction/not-aligned filters already applied), comparer-time rect allocation, double distance evaluation, and ~15 inline edge/emptiness computations while `RectCoordinate` is a bare field holder. Consolidate on `RectCoordinate` helpers (`Right`/`Bottom`/`IsEmpty`/`Intersects`) and single-pass filtering.
- **F20** — UI-thread guards are missing in several `WindowMatrix` predicates, `WindowManager`'s ctor (`RefreshCurrentWindow`), and `SolutionExplorerController.ExecuteCommand`; today they're only transitively on the UI thread. Add `ThreadHelper.ThrowIfNotOnUIThread()` per convention before a background-thread caller appears.
- **F22** — the leader state machine, `BuildSimpleKey`, and sequence-prefix matching are private inside `InputHandler` (needs AsyncPackage + MEF + WindowManager to construct). Extract a pure `LeaderSequenceMatcher`/`SimpleKeyBuilder` so the core routing is unit-testable like `OverlayKeyHandler`.
- **F23/F24** — the `IsInteresting` pre-filter has dead Ctrl-key cases (the documented completion-swallow is not implemented) and treats h/j/k/l/i as unconditionally interesting, so a vim user's most-typed letters each run a full `HandleKey` (allocations, dictionary lookups) that always returns false. Gate on `IsToolWindow` (cheap cached bool); fix the stale AGENTS.md claim.
- **F25/F26/F27** — overlay code-behind: 'I' start-insert is dead (`FocusPrompt` forces caret to end), `IsOpen=true` is set before the deferred `ShowDialog` (keys in the gap reach the editor; open-then-close in one dispatcher cycle throws), and the fire-and-forget `FilterAndUpdateAsync` has no try/catch. All three are small, deterministic fixes with unit-testable seams.
- **F28/F29/F45** — logging hygiene: `LogFileWriter.Clear()` is once-per-process so logs accumulate across overlay opens (contradicts the "current session" doc); ad-hoc `Debug.WriteLine` bypasses the shared `NeoVisualLog` path in `TelescopeController.cs:56`, `TelescopeOverlay.cs:857`, and all `VimModeTracker` reflection failures; the `[Telescope]`/`[NeoVisual]` prefix contract is asserted only in ~30 copy-pasted e2e patterns, never centralized. Centralize prefix constants and route every diagnostic through `NeoVisualLog`.
- **F30** — `SolutionExplorerController.TryMove` walks the visual tree twice per routed key (once via `TryMoveFocusedTextBox`, once again to decide fall-through); have the helper return the found box.
- **F31/F32** — `BlockCaretAdornment.Active` is never cleared when the tool window loses focus (stale block keeps drawing; unassertable), and the `IWpfTextView` branch swallows read failures silently. Log `[NeoVisual] text-motion view-read-failed key=...` and hook view focus to deactivate the adornment.
- **F33/F34/F35** — controller duplication: `GeneralToolWindowController`'s `IsTextInputType` branch is dead (text-input types never reach it), `KeyToArrowVk`+log emitter is duplicated with `SolutionExplorerController`, and `GetAsyncKeyState` is declared twice. Single-source both.
- **F36–F44** — test/tooling: test runner + `Assert` copy-pasted across the two unit suites (share one source file); `iterate-telescope.ps1` has a stale doc comment, a divergent overlay-close that contradicts the documented discipline, and triplicated helpers/paths (factor `tools/harness-common.ps1` or delete it); the e2e runner ignores `$false` scenario returns (silent false PASS); `Wait-NewLogLine` re-reads the whole log per poll (O(n²)); failures dump no log tail/step context; `Send-Text '!'` sends VK_PRIOR/PageUp (needs shift-chords); failure cleanup `Stop-Process devenv -Force` kills ALL VS instances on the machine; the fzf unit test silently passes when fzf is absent (fail or print SKIP); `dte-command.ps1` hardcodes VS paths with no vswhere fallback.
- **F46** — project-boundary clarity: the host (`MyExtension`) depends on the Telescope project for core infrastructure (`NeoVisualLog` — the extension-wide logger — and `TelescopeController`), and `Telescope.csproj` is itself VS-coupled (VS SDK + WPF overlay). The "pure-logic library" framing in the docs overstates the seam; only `OverlayKeyHandler`/`TextMotionNavigator`/`FzfFilter`/`LogFileWriter` are actually VS-free. Worth stating explicitly so the roadmap doesn't grow more VS-coupled code inside the "library".

## Verified-clean (checked this run, no action)

- **net472 compliance:** no `IReadOnlySet<T>`, `HashCode`, `MaxBy`/`MinBy`, range operators, or other .NET 5+/BCL-only APIs anywhere in `MyExtension/` or `Telescope/` (the hand-rolled `DistinctBy` is correct first-wins/null-key-safe semantics — it's just unused in production, see F18).
- **Namespace/folder hygiene:** `CardinalNavigation` namespace is confined to the intentional `CardinalMovment` folder; no typo spread.
- **ExcludeAssets="runtime":** no load-time SDK dependency in package constructors beyond the existing pattern.
- **Log system architecture:** the two-file per-run design (`*-exp.log` / `*-main.log`) and `NeoVisualLog.Log` fan-out are consistent; the divergence risk is in ad-hoc `Debug.WriteLine` bypasses (F29) and uncentralized prefixes (F45), not in the core path.

## Recommendations (ordered by effort/impact)

1. **Fix the Enter-storm** (F1 + F16 guard). One `LLKHF_INJECTED` check in `HookCallback` + a fail-fast harness assertion. Unblocks 3 live scenarios; fixes a user-visible recursion. *(small effort, highest impact)*
2. **Re-tame the hook hot path** (F2, F24). Delete the per-key `[Hook]` log; gate h/j/k/l/i interest on tool-window state. Restores the documented "few Win32 calls" contract. *(small)*
3. **Centralize vim-motion dispatch + caret styling** (F3, F4, F31, F34, F35). One key→motion dispatcher, one `ApplyCaretStyle`, one `KeyToArrowVk`, one `GetAsyncKeyState`. Kills the black-caret drift and the 4× log-contract duplication. *(medium)*
4. **Make WindowMatrix testable** (F6, F7, F18–F21). Extract a pure rect-reduction function; skip unpaired frames instead of throwing; consolidate rect math on `RectCoordinate`. *(medium)*
5. **Scale the finder seam before the roadmap** (F11, F12, F13, F15, F8, F9, F10). Preview/open contract on IFinder, stable payload ids, shared `ProjectFiles` cache, async + debounced fzf, cached preview. Do these BEFORE references/grep/implementation finders are built. *(large, prerequisite for the pending queue)*
6. **Harness hardening** (F16, F38–F42, F44, F36, F37, F43). Fix the 4 known-bug assertions, honor `$false` returns, tail-only log reads, fail-fast storm guard, targeted process kill, shared harness helpers. *(medium)*
7. **Testability extraction** (F22) and **logging hygiene** (F28, F29, F45). Pure leader-sequence matcher; route all diagnostics through `NeoVisualLog`; centralize prefix constants. *(medium)*

## Filed into progress.md

**Filed on 2026-09-19** (user approval via the `question` tool, selection "Everything
incl. harness + tooling"): F1–F16, F22, F36–F44, and F46 were appended to
`docs/progress.md` under a new "Architecture review backlog" section (27 items:
1 critical, 15 major, 11 minor) for `neovim_hub` to pick up. F16 supersedes the
known-bug expectations in the existing backlog items 1–3; F1 supersedes known-bug #4.

**Not filed** (remain report-only in this document): F17–F21, F23–F35, and F45 —
localized duplication/cleanup, dead code, missing UI-thread guards, overlay code-behind
nits, and the log-prefix-centralization item. They were not part of the approved
selection; re-file on request.