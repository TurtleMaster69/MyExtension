# MyExtension — Code Review (code-review-hub)

- **Date:** 2026-10-06 (refresh)
- **Scope:** whole repo — `MyExtension/` (core + `Navigation/` + `ToolWindows/` + `Vim/`), `Telescope/`, `tests/`, `tools/`, and the workflow docs. **20 parallel workers, no tool/step limits.**
- **Base commit:** `4118e9d` ("Code review fixes (34 findings, incl. nits) GREEN"). The prior report (2026-10-05, 34 findings) was **fully fixed in `4118e9d`** — this refresh verifies those fixes against the current code and reports the **net-new residuals + new issues**.
- **Method:** 1 whole-repo `trailmark-recon` digest (2735 nodes, 1108 proxies = 40.5%, 0 entrypoints, 117 high-blast-radius, 31 complexity hotspots) shared with every worker + **20 parallel workers** (6 `arch-auditor` — slices A/B/C/D1/D2/D3; 6 `code-review-worker` — slices A/B/C/D1/D2/D3; 3 `test-quality-reviewer` — Telescope.Tests / NeoVisual.Tests / harness+tools; 2 `docs-accuracy-reviewer` — spec/AGENTS/SKILL and progress/reviews/e2e-queue; 1 `perf-investigation`; 1 `duplication`; 1 `pinvoke-interop`). Every structural claim was verified with Trailmark (`QueryEngine.from_directory(..., language="c_sharp")`, proxy-aware `callers_of`) or LSP `incomingCalls`/`outgoingCalls`. The hub independently verified **every major finding** by direct code reading. Both offline suites pass (Telescope.Tests **297/297**, NeoVisual.Tests **212/212**). No file under `MyExtension/`, `Telescope/`, `tests/`, `tools/` was modified.
- **Prior-fix verification:** all 34 prior findings confirmed fixed — the off-thread Grep scan seam (**partially** — see M1), the cancellable Fzf gather, the thread-safe cache, the timeout-race grace, the bound `Shift+` shortcut, the Error-List/RecentFiles COM lifecycle, the block-caret focus-regain, the pane-focus resolver rename, the shared invalidation + LRU, the harness gates (LogCache tail-read, TARGET-form severity-nav, DTE polls), the test infra (`Assert.NotEqual`, per-test timeout), and the doc refresh.

## Summary

**106 findings: 0 critical, 12 major, 74 minor, 20 nit.** The post-fix code is in strong shape — the 34-fix plan landed cleanly, the pure-state-machine seams are single-sourced, logging is centralized, net472 compliance is clean, and both offline suites pass (297 + 212). The sharp edges are: (1) **the query-driven finders still freeze the UI** — the Grep scan blocks on `.GetAwaiter().GetResult()`, the Fzf finder spawns one fzf subprocess **per file per keystroke** (~105ms each → ~2.1s for 20 files), and the open-time warm-up reads every project file on the UI thread; (2) **the geometric selection pipeline is still duplicated** — `FocusTargetModel.ResolveTarget` is a near-verbatim mirror of `WindowNavigationEngine.SelectTarget` (the m6 fix only renamed it), and the two have already diverged; (3) **two real navigation/interop bugs** — a partially-overlapping window below the active editor produces a negative gap that excludes the truly-below window, and `GetLinkedWindowsList` has no per-window try/catch so one stale COM frame kills all navigation; (4) **a text-input stale-frame keyboard leak** — the mirror of the FocusGuard leak, where a stale Command Window frame claims keyboard ownership over a focused editor; and (5) a cluster of harness/test issues (an unsatisfiable re-walk assert that makes `telescope-goto` flaky, a vacuous Down-tolerance test, a wall-clock fzf timeout race) plus doc drift (the live report itself, e2e-queue.md, progress.md).

## Findings table

| id | sev | file:line | problem |
|----|-----|-----------|---------|
| M1 | major | `Telescope/Finders/GrepFinder.cs:125-135` | M1 fix incomplete: the scan runs on `Task.Run` but the caller blocks on `.GetAwaiter().GetResult()` — the UI thread still stalls for the whole scan |
| M2 | major | `Telescope/Finders/FzfFinder.cs:125-151` | One fzf subprocess spawned **per file per keystroke** (~105ms each → ~2.1s for 20 files); the FzfFilter doc's "one process per query" understates it by a factor of N |
| M3 | major | `Telescope/Finders/GrepFinder.cs:152-182` + `FzfFinder.cs:211-241` | `WarmContentCache` reads ALL project files on the UI thread at overlay open (~454ms for 100 files × 10k lines) |
| M4 | major | `Telescope/Overlay/Utils/FocusTargetModel.cs:275-332` + `MyExtension/Navigation/WindowNavigationEngine.cs:8-51` | `ResolveTarget` + `PaneRect` are a near-verbatim re-implementation of `SelectTarget` + `WindowRect` (the m6 fix only renamed); the two have already diverged (gap>=0, dropped divide) |
| M5 | major | `MyExtension/ToolWindows/WindowManager.cs:99` + `FocusGuard.cs:22` | `_textInputSurfaceFocused` is never invalidated when the editor gains focus — a stale text-input frame (Command Window) claims keyboard ownership over a focused editor (the mirror of the FocusGuard leak) |
| M6 | major | `MyExtension/Navigation/Utils/WindowFrameUtils.cs:57-65` | `GetLinkedWindowsList` reads `window.LinkedWindowFrame` (COM) with no per-window try/catch — one stale RCW throws and degrades ALL navigation to a no-op |
| M7 | major | `MyExtension/Navigation/WindowNavigationEngine.cs:58` + `WindowRect.cs:50` | Down is `c.Y > a.Y` (strictly below the active's TOP, not bottom) — a partially-overlapping window yields a negative gap that excludes the truly-below window |
| M8 | major | `MyExtension/ToolWindows/Utils/HierarchyForestBuilder.cs:29-33` + `SolutionExplorerController.cs:154` | The forest is filtered to C# source files only, but `ReturnFocusToTree` resolves the search-box query through it — a query matching a non-C# item can never resolve |
| M9 | major | `tools/harness/test-e2e.ps1:2027` | `telescope-goto`'s re-walk trigger assert is unsatisfiable (the persistent search cursor has advanced past the 0-gather line) — the 0-gather race now FAILS the scenario instead of being absorbed |
| M10 | major | `tests/Telescope.Tests/Program.cs:1289` | `Run_FzfFilter_TimeoutRace` is a wall-clock boundary race (stub `ping` ≈1s races `FilterTimeoutMs=1000`) — flaky |
| M11 | major | `tests/NeoVisual.Tests/Program.cs:2159` | `Run_WindowNavigationEngine_Down_ToleranceExcludes` passes vacuously — the `>1` tolerance was removed in 349fc05; the test passes via the last-wins tie-break |
| M12 | major | `Telescope/Overlay/TelescopeOverlay.cs:433-448` | `IsOpen = true` runs AFTER `await _fzf.IsAvailableAsync()` — a close during that await leaves `IsOpen` stuck true with `ShowDialog()` never firing (overlay permanently "open", can never reopen) |
| m1 | minor | `Telescope/Overlay/TelescopeOverlay.cs:869` | `_previewNavigator.SetText` unconditional per selection move — full-file `LineIndex` rebuild (~390µs for 10k lines) even when the file is unchanged (the prompt path guards this) |
| m2 | minor | `Telescope/Finders/Utils/FileContentCache.cs:53-64` | File I/O runs INSIDE the `_gate` lock — a cold read (~3.5ms) serializes every other cache access |
| m3 | minor | `Telescope/Finders/FzfFinder.cs:141,161-167` | The literal-fallback `LiteralLineScanner.Scan` loop + cold `GetLines` run on the UI thread — the same freeze class as M1 in the fallback path |
| m4 | minor | `Telescope/Finders/CodeIssuesFinder.cs:91-95,150-168` | The TODO scan + Error List walk run on the UI thread at open — a one-time stall on large solutions |
| m5 | minor | `Telescope/Overlay/TelescopeOverlay.cs:831-835` | `results columns=`/`results count=... boxText=` logged on EVERY render; `RenderedTextLength` re-sums all rows×cells even on selection-only renders |
| m6 | minor | `Telescope/Finders/Utils/FzfLineMapper.cs:24-35` | `Map` is O(n·m) — up to ~200k string comparisons per file per keystroke at HitCap |
| m7 | minor | `Telescope/Finders/Utils/FileContentCache.cs:56-63,77-84` | `GetLines`/`GetContent` on the same file evict each other (one entry stores either) — a mixed access pattern re-reads twice |
| m8 | minor | `Telescope/Filter/FzfFilter.cs:265-275` | A cancelled gather in the 250ms grace window can log a spurious `fzf filter failed: {msg}` |
| m9 | minor | `Telescope/Filter/FzfFilter.cs:265` | The grace `Task.Delay` is created without a cancellation token — an abandoned timer per timeout |
| m10 | minor | `MyExtension/Hooks/GlobalKeyboardHook.cs:94` | `IsVisualStudioFocused()` runs before the `isKeyDown` check — every key-up pays the two Win32 calls |
| m11 | minor | `MyExtension/Input/InputHandler.cs:481-486` | `DateTime.UtcNow` read on every key-down for the 250ms sentinel interval (a no-op in production) |
| m12 | minor | `MyExtension/Vim/Utils/VimBufferSubscriptions.cs:123-141` | `_bufferToTextBuffer` leaks entries for detached views sharing a text buffer; `VsVimModeSource.Detach` skips `RemoveClosed` when `lastView` is false |
| m13 | minor | `MyExtension/Vim/Utils/VimModeSource.cs:170-188` | `OnBufferClosed` re-reads `get_VimTextBuffer` via reflection on a closing buffer — a reflection failure leaks the `SwitchedMode` subscription |
| m14 | minor | `MyExtension/Input/Utils/KeybindingConfig.cs:242` | `ParseLeader` accepts physical modifier keys (LControlKey etc.) as a leader — accepted yet non-functional |
| m15 | minor | `MyExtension/Package/RoslynGatherers.cs:325` | The DTE `TextSelection` fallback doesn't clamp the column to the line length — a caret in virtual space resolves the wrong symbol |
| m16 | minor | `MyExtension/Hooks/GlobalKeyboardHook.cs:103` | The callback never gates on `HC_ACTION` — a peeked event (HC_NOREMOVE) is processed as a real key-down |
| m17 | minor | `MyExtension/Package/Utils/RecentFilesGatherer.cs:51`, `ErrorListGatherer.cs:160`, `WindowManager.cs:399` | `Dispose()` doesn't call `ThrowIfNotOnUIThread()` before the COM event unhook |
| m18 | minor | `MyExtension/Hooks/Utils/KeyInjection.cs:17` | Stale doc: "we only ever inject *arrows*" — the code now injects VK_RETURN/VK_F2/VK_ESCAPE too |
| m19 | minor | `MyExtension/Hooks/GlobalKeyboardHook.cs:100` | `Marshal.ReadInt32(lParam)` with no `lParam == IntPtr.Zero` guard — an exception in the hook callback is fatal (Windows silently removes the hook) |
| m20 | minor | `Telescope/Controller/TelescopeController.cs:102-114` | `Dispose()` swallows an off-UI-thread exception and leaks the open overlay (the modal keeps swallowing keys) |
| m21 | minor | `MyExtension/ToolWindows/Utils/FocusKeeper.cs:25` | A queued `DispatcherTimer` tick is not cancelled by `Stop()` — a superseded keeper can re-assert the old target once |
| m22 | minor | `MyExtension/ToolWindows/Utils/ToolWindowTypeResolver.cs:168-169` | `FindResults1`/`FindResults2` are classified as text-input (they are read-only lists) — they start in input mode and inherit the `OwnsKeyboard` exemption |
| m23 | minor | `Telescope/Logging/Utils/PaneFailureTracker.cs:14` | `_emitted` is an unsynchronized bool — the one-time fallback can emit twice under concurrent UI/background loggers |
| m24 | minor | `Telescope/Finders/GrepFinder.cs:152-194` + `FzfFinder.cs:211-253` | `WarmContentCache` + WarmFile are byte-identical in both finders (the m7 fix extracted `EnsureSolutionCache` but not the warm-up) |
| m25 | minor | `Telescope/Finders/Utils/GrepHit.cs:8` + `FzfHit.cs:8` | Byte-identical hit models (`FileLocation` + `LineText`) |
| m26 | minor | `Telescope/Finders/Utils/DefinitionHit.cs:10` + `ImplementationHit.cs:10` | Identical hit models (`FileLocation` + `SymbolName` + `Kind`) |
| m27 | minor | `Telescope/Overlay/Utils/FinderColumns.cs:123-141` | `Grep()` and `Fzf()` catalogs are byte-identical |
| m28 | minor | `Telescope/Overlay/Utils/FinderColumns.cs:70-93` | `Files()` and `Recent()` are near-identical |
| m29 | minor | `Telescope/Finders/GrepFinder.cs:25`, `FzfFinder.cs:29`, `RecentFilesFinder.cs:28` | `HitCap = 200` is a separate constant in three finders |
| m30 | minor | `Telescope/Finders/GrepFinder.cs:51`, `FzfFinder.cs:57`, `CodeIssuesFinder.cs:56`, `FileFinder.cs:51` | The 4-constructor test-seam sprawl is re-implemented per finder (the newer finders use the clean host-injected single-ctor pattern) |
| m31 | minor | `Telescope/Overlay/Utils/ResultColumn.cs:146` + `ResultsFormatter.cs:42-46` | `ColumnVisibilityModel.VisibleIdsJoined` is a dead twin of `ResultsFormatter.ColumnsIdList` (test-only; stale doc comment) |
| m32 | minor | `Telescope/Overlay/Utils/ResultRowCells.cs:19-29` | The 2-arg `Compute` overload has no production callers |
| m33 | minor | `MyExtension/Package/Utils/ErrorListGatherer.cs:109-147` + `Telescope/Finders/CodeIssuesFinder.cs:178-213` | Both iterate `ErrorItems` with the same per-item try/catch (cross-slice duplication) |
| m34 | minor | `MyExtension/ToolWindows/Utils/TextMotionHelper.cs:277-290` + `Telescope/Logging/Utils/DiagnosticLog.cs:22-40` | `SanitizeSample` is byte-identical to the shared `DiagnosticLog.SanitizeText` (never migrated) |
| m35 | minor | `MyExtension/Input/InputHandler.cs:115-135` | Two near-identical `ShouldRouteToolWindowKey` overloads remain (the n3 fix collapsed the call sites but left both) |
| m36 | minor | `MyExtension/Input/InputHandler.cs:422` + `FocusGuard.cs:50-52` | The R10 shift gate is inlined in `TryRouteToolWindowKey` while the pure 7-arg `FocusGuard.ShouldRouteToolWindowKey` overload is now TEST-ONLY |
| m37 | minor | `MyExtension/Input/Utils/LeaderSequenceMatcher.cs:171` + `SimpleShortcutMatcher.cs:27` | `LeaderResult.Action` / `SimpleShortcutResult.Action` are write-only dead state (the matcher already invoked the action) |
| m38 | minor | `MyExtension/Input/InputHandler.cs:398,403` | `CurrentController` resolved twice per key in `TryRouteToolWindowKey` (the n3 fix addressed the double DECISION, not the double resolution) |
| m39 | minor | `Telescope/Overlay/TelescopeOverlay.cs:149,713` + `ColumnWidths.cs:41,44` | Magic numbers duplicate the centralized constants (`Width = 760`, `- 18`) |
| m40 | minor | `Telescope/Overlay/Utils/ResultColumn.cs:129,132` | `ColumnVisibilityModel.Ids` and `Catalog` are dead (the chooser iterates `_activeCatalog` directly) |
| m41 | minor | `Telescope/Overlay/Utils/Panes/PaneSelectionSync.cs:15` | The class is an empty shell (the n6 fix deleted `Steps` but left the class + a reflection test pinning its absence) |
| m42 | minor | `Telescope/Overlay/TelescopeOverlay.cs:87,1376` | `_chooserMenu` is assigned and nulled but never read |
| m43 | minor | `Telescope/Overlay/TelescopeOverlay.cs:46,673,713` | `_fixedWidthSum` is only ever set to 0 — vestigial |
| m44 | minor | `Telescope/Overlay/Utils/Panes/IPane.cs:33` | `IPane.IsFocusable` is never read by production code |
| m45 | minor | `Telescope/Overlay/Utils/OverlayKeyHandler.cs:23-24` + `TelescopeOverlay.cs:1237-1238` | `OverlayKey.CtrlH`/CtrlL enum members + `MapKey` cases are dead (the focus machine consumes the Ctrl chords first) |
| m46 | minor | `Telescope/Overlay/Utils/PromptMotionRouter.cs:32-40` + `TextMotionDispatcher.cs:168` | The key is mapped TWICE per motion keystroke, and the insert-placement mapping is duplicated with a drift (AfterCaret vs Current) |
| m47 | minor | `MyExtension/ToolWindows/SolutionExplorerController.cs:214` | `TryMove` walks the visual tree per routed key, bypassing the M1/C3 focus-change caching |
| m48 | minor | `MyExtension/ToolWindows/GeneralToolWindowController.cs:84-87` | `IsTextInputType` is a pure pass-through wrapper around `ToolWindowTypeResolver.IsTextInputType` |
| m49 | minor | `MyExtension/ToolWindows/Utils/HierarchyForestBuilder.cs:29-30` + `HierarchyResolver.cs:49` | The C#-file filter is applied twice (in `Build` and again in `FirstSourceFilePath`) |
| m50 | minor | `MyExtension/Package/MyExtensionPackage.cs:173` | `_errorListGatherer!` null-forgiving is inconsistent with the `_launcher == null` guard — a partial finders failure degrades the whole hook path |
| m51 | minor | `tests/Telescope.Tests/Program.cs:5051` | `Run_FzfFilter_KillRegisteredBeforeWrite` uses a fixed `Thread.Sleep(200)` — on a slow machine the write may not have blocked, passing vacuously |
| m52 | minor | `tests/Telescope.Tests/Program.cs:997-1055` | The two FlushTimer tests leak the injected `FakeTimer` into the static `LogFileWriter._flushTimer` — the real ~200ms flush timer is disabled for later tests |
| m53 | minor | `tests/Telescope.Tests/Program.cs:4624,4714,5003,5018,3880,3939,5343` | Reflection-based implementation coupling (asserts on private field/method names or their absence) |
| m54 | minor | `Telescope/Finders/Utils/LiteralLineScanner.cs:14` | `LiteralLineScanner.Scan` has no direct unit test (null/empty/cap guards only covered indirectly) |
| m55 | minor | `tests/NeoVisual.Tests/Program.cs:608,3160` | m17 residual: the reflection tests `Run_WindowManager_DefaultControllerCache_ReturnsCachedInstance` + `Run_GetController_SameInstance` remain (the hermetic `NoReflection` test was added but the old ones never removed) |
| m56 | minor | `tests/NeoVisual.Tests/Program.cs:1435,1505,1518` | FocusGuard tests assert the identical expression twice (the N46 fix missed three) |
| m57 | minor | `tests/NeoVisual.Tests/Program.cs:3397` | `Run_BlockCaretState_DesiredVsRendered` pins private fields via reflection and never invokes the real `OnLostFocus`/`OnGotFocus` handlers |
| m58 | minor | `tests/TestRunner.cs:93-98` | The per-test timeout abandons a thread that may be mid-mutation of shared static state (dispatcher, log paths, guard) |
| m59 | minor | `tests/NeoVisual.Tests/Program.cs:2872` | `Run_IsKeyOfInterest_OverlayOpenShortCircuit` builds via `FormatterServices.GetUninitializedObject` + reflection into four private fields |
| m60 | minor | `MyExtension/Input/Utils/KeybindingConfig.cs:135` | The multi-source `Merge` (defaults + user overrides) is untested — the core of the user-config feature |
| m61 | minor | `tests/NeoVisual.Tests/Program.cs:623,2330,3163,3342,3467,3529,3607,3640` | 8 tests mutate the static `ThreadHelper.uiThreadDispatcher` via reflection; the restore only runs on a normal return |
| m62 | minor | `tools/harness/test-e2e.ps1:1596` | m15 residual: `preview tokens=\d+` is still presence-only (accepts `tokens=0`) |
| m63 | minor | `tools/harness/test-e2e.ps1:1010-1014` | Conditional test logic: the four severity-nav outcome assertions branch on runtime Error List state — a timeout with warnings==0 silently takes the no-op branch |
| m64 | minor | `tools/harness/test-e2e.ps1:1467` | `explorer-open-navigation`'s `editor-view-opened file=.*\.cs` is order-dependent on the first source file NOT being pre-opened |
| m65 | minor | `tools/lint/check-doc-refs.ps1:48-56` + `docs/e2e-queue.md:157,164` | `e2e-queue.md` is not in the lint doc set and fails with 2 unresolved backticked refs (`GotoProbe`) |
| m66 | minor | `docs/e2e-queue.md:8` | "Status: EMPTY (2026-10-04)" header contradicts the 8 queued gates in the same file |
| m67 | minor | `docs/e2e-queue.md:130-167` | E2E-RC-1/2 + E2E-GOTO-1/2 still listed as queued with no DISCHARGED annotation (both plans GREEN 2026-10-04) |
| m68 | minor | `docs/progress.md:10-15` | Header "Updated: 2026-10-05 · Last item: Gap 4" is stale (the last item is the 34-findings plan, 2026-10-06) |
| m69 | minor | `docs/progress.md:257-258` | Baseline counts 297/212 are correct but the "(after Gap 4)" attribution is stale (they are post-34-findings) |
| m70 | minor | `docs/progress.md:409` | RUN ORDER lists "gap 6" as pending — it is DONE (GREEN 2026-10-04) |
| m71 | minor | `docs/reviews/code-review.md:11,225` | The live report lists 34 findings as open (all fixed in 4118e9d) + stale counts 288/200 (actual 297/212) |
| m72 | minor | `.opencode/skills/vs-extension-dev/SKILL.md:240-241` | "DOWN uses a `> 1` pixel tolerance" — the tolerance was removed in 349fc05 (`c.Y > a.Y`) |
| m73 | minor | `.opencode/skills/vs-extension-dev/SKILL.md:92` | The `Panes/` parenthetical misattributes the geometric resolver (it lives in `FocusTargetModel`) |
| m74 | minor | `.opencode/agent/neovim_review_hub.md:178` + `.opencode/agent/code-review-hub.md:307` | Slice-B file lists still reference the deleted `LinqExtensionMethods.cs` and omit four current files |
| n1 | nit | `MyExtension/Navigation/WindowNavigator.cs:27` | Stale comment line ref ("InputHandler.cs:554" — now :583) |
| n2 | nit | `tests/TestRunner.cs:101` | Failure output prints only the message, no stack trace |
| n3 | nit | `tests/TestRunner.cs:93` | The per-test timeout is a "stop waiting", not a kill — no isolation guarantee |
| n4 | nit | `tests/NeoVisual.Tests/Program.cs:1392` | `PositiveActionKeyCount` dead constant |
| n5 | nit | `tests/NeoVisual.Tests/Program.cs:356` | `Run_KeyNames_RoundTrip_WindowPrefix` is an eager test (three scenarios in one) |
| n6 | nit | `tests/NeoVisual.Tests/Program.cs:2173` | Stale comments in the Down-tolerance tests describe the removed tolerance |
| n7 | nit | `tests/NeoVisual.Tests/Program.cs:563` | Pervasive `Assert.True(x == y, "msg")` instead of `Assert.Equal` (20 sites) — failures show no actual/expected |
| n8 | nit | `tests/NeoVisual.Tests/Program.cs:3679` | `Run_TestRunner_Timeout` adds ~5s and leaves a 10-minute sleeping thread alive |
| n9 | nit | `tools/harness/iterate-telescope.ps1:333,338` | Fixed 1200ms sleeps for focus + fzf settle (sleepy-test smell) |
| n10 | nit | `tools/lint/check-doc-content.ps1:160` | DOC-66-3's `$baselineRightAttr` is a moving-target allowlist of ~11 prose patterns |
| n11 | nit | `docs/progress.md:129` | Dangling fragment "of the user-chosen **"smallest first"** run order" |
| n12 | nit | `docs/progress.md:335` | "In-progress" cites Gap 3 as the last completed item (it's the 34-findings plan) |
| n13 | nit | `MyExtension/ToolWindows/WindowManager.cs:263-270` | `ResolveController` allocates a throwaway `Dictionary` per call |
| n14 | nit | `MyExtension/ToolWindows/Utils/WindowTypeProbe.cs:11` | `ShouldLogFailure(hr) => hr < 0` is a single-use one-line wrapper |
| n15 | nit | `Telescope/Controller/GotoDispatcher.cs:22` | `Decide` is a one-line `hitCount == 1 ? DirectJump : OpenOverlay` wrapper (the n6 pattern) |
| n16 | nit | `Telescope/Overlay/TelescopeOverlay.cs:957` | `EnterInsert` logs a spurious `focus target=Input` even when already on Input |
| n17 | nit | `Telescope/Overlay/Utils/FocusTargetModel.cs:286` | `ResolveTarget` allocates a `List<Candidate>` per Ctrl+H/J/K/L move |
| n18 | nit | `tests/Telescope.Tests/Program.cs:2061` | `Run_LineIndex_LineOfMatchesNavigator` is a cross-implementation check where both could share the same defect |
| n19 | nit | `Telescope/Finders/DefinitionFinder.cs:40-71` + `ImplementationFinder.cs:41-63` | Near-identical finder bodies (single-ctor pattern, display contract) |
| n20 | nit | `Telescope/Logging/NeoVisualLog.cs:109-112` | The `Log` comment claims both per-run files but the debug duplication is opt-in (`NEOVISUAL_DEBUG_DUP`) |

## Detailed findings

### M1 (major) — the Grep scan still blocks the UI thread (M1 fix incomplete)

- **Where:** `Telescope/Finders/GrepFinder.cs:125-135` (the scan loop), with the UI-thread assert at `:108`.
- **What's wrong:** The 34-fix made the scan run on `Task.Run`, but the caller does `.GetAwaiter().GetResult()` — a synchronous block. `RefreshQueryDrivenAsync` (TelescopeOverlay.cs:505-508) resumes on the WPF `SynchronizationContext`, so `GetCandidates` runs on the UI thread and blocks on `GetResult` for the full scan. The comment at `:118-123` claims "so the UI thread is not blocked" — false. Three independent workers (arch-auditor D2, code-review-worker D2, perf) reported this.
- **Why it bites:** On a real (large) solution the Grep overlay freezes for the full scan on every settled keystroke — hundreds of ms of unresponsive UI, keystrokes queueing behind the blocked thread. The e2e scratch solution is too small to catch it, so this ships green.
- **Fix:** Override `GetCandidatesAsync` in `GrepFinder` to `await Task.Run(...)` the scan (no blocking), keeping the DTE enumeration + `EnsureSolutionCache` on the UI thread, mirroring `FzfFinder`. The overlay's `_queryGeneration` guard already discards stale results.

### M2 (major) — the Fzf finder spawns one fzf subprocess per file per keystroke

- **Where:** `Telescope/Finders/FzfFinder.cs:125-151` — `await _fzf.FilterAsync(lines, query, cancellationToken)` sits inside the `foreach (string path in files)` loop.
- **What's wrong:** A query over N files spawns N `fzf --filter` processes. Measured (perf worker): **~105ms per spawn** → 20 files ≈ **2.1s** of latency per keystroke (sequential awaits), and every keystroke kills the previous batch. `FzfFilter.cs:26-28` documents "a short-lived process per query" — the finder is per-file, N× the documented cost.
- **Why it bites:** The Fzf finder (`Space+F+Z`) is effectively unusable on any multi-file solution — results lag seconds behind typing; the 200ms debounce + cancellation churn spawns/kills dozens of processes per query.
- **Fix:** Batch all files' lines into ONE `fzf --filter` call (fzf ranks across all candidates on stdin) and map the ranked output back to (file, line) via `FzfLineMapper` with file-boundary tracking; or adopt fzf `--listen` (persistent server) / an in-process matcher.

### M3 (major) — the open-time warm-up reads every project file on the UI thread

- **Where:** `Telescope/Finders/GrepFinder.cs:152-182` (`WarmContentCache`) and `FzfFinder.cs:211-241` (the duplicated copy).
- **What's wrong:** `WarmContentCache` reads ALL project files synchronously on the UI thread at overlay open. Measured (perf worker): **~454ms UI-thread block** for 100 files × 10k lines (cold `ReadAllLines` ≈ 3.45ms/file).
- **Why it bites:** First Grep/Fzf open freezes the modal overlay for the whole warm-up read; the freeze scales linearly with file count and size.
- **Fix:** Warm the cache off the UI thread (`Task.Run`/`await`), or drop the eager warm and let the first gather populate it lazily (the gather already runs off-thread for Grep).

### M4 (major) — the geometric selection pipeline is still duplicated (m6 residual)

- **Where:** `Telescope/Overlay/Utils/FocusTargetModel.cs:275-332` (`ResolveTarget`) + `:61-113` (`PaneRect`) + `:346-371` (`PerpendicularAxis`/`IsInDirection`/`IsAligned`) vs `MyExtension/Navigation/WindowNavigationEngine.cs:8-51` + `Utils/WindowRect.cs:6-56`.
- **What's wrong:** The 34-fix (BP-12/m6) only RENAMED `SelectTarget` → `ResolveTarget`; the pipeline is still a near-verbatim re-implementation (same `Candidate` struct, same minGap/adjacency/`>=`-last-tie loop). The two have ALREADY diverged: the pane copy adds `gap >= 0` and drops the DPI divide. Trailmark `callers_of("proxy.unresolved:WindowNavigationEngine.SelectTarget")` = {`WindowNavigator.NavigateInDirection` + 13 tests} — no `FocusTargetModel` edge, so the SKILL.md claim that "the collapsed FocusTargetModel runs the WindowNavigationEngine pipeline" is a mirror, not a shared call.
- **Why it bites:** Two pinned behavioral contracts (e2e `telescope-focus-panes` + `neovisual-window-nav`) sit on two copies of the same algorithm that have already drifted. A future algorithm change (tie-break, tolerance, a new filter) propagates to only one surface — the exact drift class that produces "works in window nav, broken in overlay focus" bugs.
- **Fix:** Extract the pure geometric engine (rect + direction + selection) into a dependency-free shared assembly both projects reference (MyExtension already references Telescope — the "NOT shared" comment is wrong), parameterize the two deviations (allowNegativeGap, divide), and have `WindowNavigationEngine` delegate.

### M5 (major) — text-input stale-frame keyboard leak (the mirror of the FocusGuard leak)

- **Where:** `MyExtension/ToolWindows/WindowManager.cs:99` (`TextInputSurfaceFocused`) + `FocusGuard.cs:22` (`OwnsKeyboard`).
- **What's wrong:** `_textInputSurfaceFocused` is cached only in `OnWindowFocusChanged` and never invalidated when the main editor gains focus. When the `SEID_WindowFrame` event goes stale (the documented staleness the FocusGuard exists for), a text-input tool window (Command Window) left in normal mode keeps `IsTextInputType=true` + `TextInputSurfaceFocused=true`, so `FocusGuard.OwnsKeyboard` returns true, `EditorFocusedVeto` is disabled, and `ShouldRouteToolWindowKey` stays true while the main editor holds focus. `TryRouteToolWindowKey` then routes h/l/w/b/e/a/A/I to the controller, and `TextMotionHelper.TryMoveFocusedSurface` matches `Keyboard.FocusedElement is IWpfTextView` — the MAIN editor — and applies the motion to its caret, swallowing the key. In VsVim insert mode h/l/w/b/e/a/A/I are eaten instead of typed.
- **Why it bites:** This is the mirror of the Solution Explorer leak the FocusGuard was built to fix, but the text-input exemption leaves it open. `neovisual-editor-insert` doesn't exercise it (it opens the editor directly, never a stale Command Window frame), so it ships green.
- **Fix:** Invalidate `_textInputSurfaceFocused` when the editor gains focus (subscribe to the same `Got/LostAggregateFocus` events VimModeTracker uses, or gate `OwnsKeyboard` on `!IsEditorFocused` for non-input-mode text-input controllers).

### M6 (major) — one stale COM frame kills all navigation (F7 residual in the linking path)

- **Where:** `MyExtension/Navigation/Utils/WindowFrameUtils.cs:57-65` (`GetLinkedWindowsList`).
- **What's wrong:** The loop reads `window?.LinkedWindowFrame` (COM) with no per-window try/catch. One stale/disconnected RCW throws, propagating through `LinkedTo` → `BuildActiveWindows` → the ctor catch, degrading ALL navigation to a no-op (logged) until the next focus change re-enumerates.
- **Why it bites:** This is the same class as the F7/R4 per-frame isolation fix in `Enumerate`/`TryCreateAdapter` ("one stale frame must not kill all navigation"), but the linking path was not hardened — a single window closed between focus changes silently disables window navigation for the whole window-set.
- **Fix:** Wrap each `window.LinkedWindowFrame` read (and the `CompareWindows` Caption/Type reads) in a per-window try/catch that skips the bad window.

### M7 (major) — Down navigation can select an overlapping window and skip the truly-below one

- **Where:** `MyExtension/Navigation/WindowNavigationEngine.cs:58` (`IsInDirection` Down = `c.Y > a.Y`) + `Utils/WindowRect.cs:50` (`GapTo` Down = `Y - other.Bottom`).
- **What's wrong:** `IsInDirection` for Down is strictly below the active's TOP, not bottom. A candidate with `a.Y < c.Y < a.Bottom` (a floating window partially overlapping the active editor — VS allows floating overlap) passes the direction filter, and `GapTo` yields a NEGATIVE gap (`c.Y - a.Bottom`). That negative gap becomes `minGap`, and the divide window `[minGap, minGap + divide]` then EXCLUDES the truly-below window (positive gap > upperBound).
- **Why it bites:** With a floating window partially overlapping the active editor, Down selects the overlapping window and skips the window actually below — a wrong navigation target in a realistic layout.
- **Fix:** Use `c.Y > a.Bottom` for Down (and `c.Bottom < a.Y` for Up) so only windows strictly beyond the active's edge qualify (gap >= 0), or clamp `minGap` to >= 0.

### M8 (major) — the C#-only forest breaks non-source search-box resolution

- **Where:** `MyExtension/ToolWindows/Utils/HierarchyForestBuilder.cs:29-33` + `SolutionExplorerController.cs:154` (`ReturnFocusToTree`).
- **What's wrong:** `HierarchyForestBuilder.Build` filters the forest to C# files only, but `ReturnFocusToTree` resolves the search-box query through that same forest via `HierarchyResolver.FirstPathMatching` → `HierarchyWalker.FirstPathContaining`. A query matching a non-C# item (.resx, .json, .xaml) can never resolve, so focus returns to the tree without selecting the matched node.
- **Why it bites:** The search-box→tree handoff silently degrades for non-source files; `g` (`SelectFirstSourceFile`) also only sees top-level files + already-expanded folders (a collapsed folder's `UIHierarchyItems` is empty — `MapChildren` never sets `Expanded = true`). The e2e passes only because the seed has top-level C# files.
- **Fix:** Expand each folder before recursing in `MapChildren`, and build the forest unfiltered (or add a query-mode builder) applying the C#-file filter only in `FirstSourceFilePath`.

### M9 (major) — `telescope-goto`'s re-walk trigger assert is unsatisfiable

- **Where:** `tools/harness/test-e2e.ps1:2027`.
- **What's wrong:** `Wait-NewLogLineAfter` starts at `max($fromIndex, $script:LogSearchedTo)` and advances the cursor to the cache end on every no-match scan. In `telescope-goto` Part 1, the 15s wait for `goto-direct` (line 2020) times out on the 0-gather race, scanning and cursor-advancing past the `definitions gathered count=0` line; the trigger assert at line 2027 reuses the same `$idx` snapshot but searches from the cursor (past the line), so it always throws "never saw: first goto attempt gathered 0 definitions". The re-walk (attempt 2) is dead code — the 0-gather race now FAILS the scenario instead of being absorbed.
- **Why it bites:** Exactly the flakiness the n10 fix was meant to remove — the scenario fails on the first-attempt 0-gather race instead of retrying.
- **Fix:** Scan the LogCache directly for the 0-gather signature in the `[idx, count)` window (the `Assert-NoEnterStorm` idiom), or take a fresh `Get-LogCacheIndex` snapshot immediately before the trigger assert and reset `$script:LogSearchedTo` to it.

### M10 (major) — `Run_FzfFilter_TimeoutRace` is a wall-clock boundary race

- **Where:** `tests/Telescope.Tests/Program.cs:1289-1308`.
- **What's wrong:** The stub (`ping -n 2 127.0.0.1` ≈ 1s) races `FilterTimeoutMs=1000`; pass/fail depends on real subprocess timing. On a slow/loaded machine the stub exceeds 1s + the 250ms grace, the process is killed, the unfiltered 2-item list is returned, and `Assert.Equal(1, result.Count)` fails.
- **Why it bites:** A flaky test that sits deliberately at a timing boundary — it will fail intermittently on slow CI machines.
- **Fix:** Make the stub's delay deterministic (inject a clock or a stub that completes a fixed sub-timeout duration) and assert the boundary fast path without real `ping` timing.

### M11 (major) — `Run_WindowNavigationEngine_Down_ToleranceExcludes` passes vacuously

- **Where:** `tests/NeoVisual.Tests/Program.cs:2159-2169`.
- **What's wrong:** The test claims candidate 0 (Y=101) is "EXCLUDED by the >1 tolerance", but the tolerance was removed in 349fc05 (`WindowNavigationEngine.cs:58` is now `c.Y > a.Y`). Both candidates pass the direction filter; `Assert.Equal(1, ...)` is satisfied by the last-wins tie-break (both have adjacency 100). The comment is false. Three independent workers (arch-auditor B, code-review-worker B, test-quality NeoVisual ×2) reported this.
- **Why it bites:** The test gives false confidence that the asymmetric-tolerance bug is guarded — it passes with or without the tolerance, and its name/comment actively mislead a reader into believing the `>1` filter exists. It also directly contradicts `Run_WindowNavigationEngine_Down_OnePixelGapAccepted` (2171), which pins the actual no-tolerance contract.
- **Fix:** Delete it (the current behavior is already pinned by `Run_WindowNavigationEngine_Down_OnePixelGapAccepted`), or rewrite it to assert the actual current contract (both 1px and 2px gaps accepted, last-wins tie) with a name that matches.

### M12 (major) — the overlay can get stuck "open" but never shown

- **Where:** `Telescope/Overlay/TelescopeOverlay.cs:433-448`.
- **What's wrong:** `IsOpen = true` + `_showState.RequestShow()` run AFTER `await _fzf.IsAvailableAsync()` (line 399). If `CloseOverlay()` runs during that await (external `TelescopeController.Close()`/`Dispose()`, or a re-entrant close while `JTF.Run` pumps the UI thread), it sets `IsOpen=false` + `_showState.Close()`, then this code re-sets `IsOpen=true` while `ShouldShowDialog()` stays false (`_requested && !_closed` = true && false) — so `ShowDialog()` never fires but `IsOpen` is stuck true.
- **Why it bites:** The overlay is permanently "open" but never shown: the controller's `IsOpen` reports true, every subsequent `Open()` returns early ("already showing"), and the user can never open Telescope again until the package restarts.
- **Fix:** Set `IsOpen=true` + `RequestShow()` BEFORE the await (and bail after the await if `_showState` says closed), or re-check `_showState.ShouldShowDialog()` after the await and reset `IsOpen=false` on a closed state.

## Cross-cutting (slice F — hub-conducted)

- **Duplication between slices:** the big clusters stay resolved (`TextMotionDispatcher` single dispatch, `BlockCaretStyle` single caret renderer, `KeyToArrowVk`/`GetAsyncKeyState` single-sourced, the retired `SyntaxHighlighter` fully gone, the shared `TestRunner`). Remaining: the mirrored `FocusTargetModel.ResolveTarget` vs `WindowNavigationEngine.SelectTarget` (M4), the duplicated `WarmContentCache`/WarmFile warm-up (m24), the byte-identical hit models (m25/m26), the identical column catalogs (m27/m28), the triplicated `HitCap` (m29), the 4-ctor test-seam sprawl (m30), the cross-slice `ErrorItems` iteration (m33), the `SanitizeSample` twin (m34), the two `ShouldRouteToolWindowKey` overloads (m35), and the inlined-vs-pure shift gate (m36).
- **net472/BCL consistency:** clean — no `IReadOnlySet<T>`/modern-BCL usage anywhere (all workers verified).
- **Namespace/folder hygiene:** clean post-restructure; the only stale references are the two agent slice-B lists still naming the deleted `LinqExtensionMethods.cs` (m74).
- **Log-format drift across the two projects:** the remaining drift points are the two `[Telescope]`-prefix helpers (n5 residual — documented as the pinned contract), the spurious `fzf filter failed: timeout` line on the M11-race/grace-cancel path (m8), and the `NeoVisualLog.Log` comment vs opt-in debug duplication (n20); the `[Telescope]`/`[NeoVisual]`/`[Hook]`/`[MyExtension]` prefix contract is otherwise centralized in `DiagnosticLog`.
- **Hook-path cost:** `InputHandler.HandleKey` (complexity 13) + the double `CurrentController` resolution (m38) + the `IsVisualStudioFocused()`-before-`isKeyDown` (m10) + the per-key `DateTime.UtcNow` sentinel read (m11) + the double `FindFocusedTextBox` walk (m47) are the remaining hot-path concerns; the per-key `[Hook]` log stays fixed.

## Verified-clean (checked this run, no action)

- **All 34 prior findings are fixed** — verified by direct code reading + the workers (off-thread Grep seam partially — see M1; cancellable Fzf gather, thread-safe cache, timeout-race grace, bound Shift+ shortcut, Error-List/RecentFiles COM lifecycle, block-caret focus-regain, pane-focus resolver rename, shared invalidation + LRU, harness gates T1/T3/T4/T5/T6, shared TestRunner, Assert.NotEqual, per-test timeout).
- **Vim-motion dispatch is single-sourced** through `TextMotionDispatcher` (LSP-verified: `TextMotionHelper.MapMotion` → `TextMotionDispatcher.MapKey`).
- **Logging is centralized** — the only `Debug.WriteLine` in Telescope is the opt-in path inside `NeoVisualLog`.
- **Both offline suites pass** — Telescope.Tests **297/297**, NeoVisual.Tests **212/212** (hub + workers verified by running them).
- **Doc-reference lint PASS** (0 unresolved across 29 docs) and **doc-content lint PASS** (12/12) — the mechanical gates are green; the drift is content-level (m65-m74).
- **net472 compliance:** no modern-BCL APIs anywhere in `MyExtension/` or `Telescope/`.
- **The Enter-storm InjectedKeyGuard, the FocusGuard leak guard, the `stale-toolwindow` sentinel, the VsVim GetInterfaceMap interop, and the Roslyn `JoinableTaskFactory.Run` discipline** all verified intact.

## Recommendations (ordered by effort/impact)

1. **Fix the query-driven UI freeze + subprocess storm (M1 + M2 + M3 + m2/m3/m4)** — make `GrepFinder.GetCandidatesAsync` truly async (no `GetResult`), batch the Fzf per-file spawns into one fzf invocation (or `--listen`), warm the cache off-thread, and move the file I/O out of the `FileContentCache` lock. Medium; removes the biggest hazards in the modular core.
2. **Fix the two navigation/interop bugs (M6 + M7)** — per-window try/catch in `GetLinkedWindowsList`, and `c.Y > a.Bottom` for Down. Small; both are real user-visible navigation defects.
3. **Fix the text-input stale-frame leak (M5)** — invalidate `_textInputSurfaceFocused` on editor focus gain. Small; closes the mirror of the FocusGuard leak.
4. **Resolve the mirrored pane pipeline (M4)** — extract the shared geometric selection or replace with a direction table. Medium; the biggest architecture win remaining.
5. **Fix the harness flakiness (M9 + M10 + M11)** — the `telescope-goto` re-walk assert, the fzf timeout race, and the vacuous Down-tolerance test. Small harness/test changes.
6. **Fix the overlay IsOpen race (M12)** — set `IsOpen` before the await. Small.
7. **Refresh the docs (m65-m74)** — e2e-queue.md (lint set + header + DISCHARGED annotations), progress.md (header/baseline/run-order), the live code-review.md, SKILL.md (Down-tolerance + Panes/ parenthetical), and the two agent slice-B lists. Trivial.
8. **Clean up the duplication cluster (m24-m36, m41-m46)** and the nits (n1-n20) opportunistically.

## Filed into progress.md

**Nothing filed yet** — pending the user's Step-4 selection (see the question). Cross-references to `docs/reviews/architecture-review.md` and the prior `docs/reviews/code-review.md`: M1 is the residual of the prior M1 (the seam was made pure but the caller loop blocks on `GetResult`); M2 is a new angle on the fzf hardening (the kill-before-write fix is present but the per-file spawn is N× the documented cost); M4 is the residual of the prior m6 (the mirrored pipeline survives inside `FocusTargetModel`); M6 is the F7/R4 per-frame isolation residual in the linking path; M7 is a new navigation-algorithm bug; M11 is the vacuous Down-tolerance test (the tolerance was removed in 349fc05); M12 is a new overlay open/close race.
