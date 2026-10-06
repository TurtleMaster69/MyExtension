# MyExtension — Code Review (code-review-hub)

- **Date:** 2026-10-05 (refresh)
- **Scope:** whole repo — `MyExtension/` (core + `Navigation/` + `ToolWindows/` + `Vim/`), `Telescope/`, `tests/`, `tools/`, and the workflow docs. **Precision focus, no tool limitations.**
- **Base commit:** `cea9798` ("Code review fixes (45 findings, incl. nits) GREEN"). The prior report (2026-10-05, 45 findings) was **fully fixed in `cea9798`** — this refresh verifies those fixes against the current code and reports the **net-new residuals + new issues**.
- **Method:** 1 whole-repo `trailmark-recon` digest (2595 nodes, 1016 proxies = 39.2%, 0 entrypoints, 104 high-blast-radius, 29 complexity hotspots) shared with every worker + 6 parallel workers (2 `arch-auditor` — slices A–C and D; 2 `code-review-worker` — slices A–C and D; 1 `test-quality-reviewer` — slice E; 1 `docs-accuracy-reviewer` — the docs) + hub-conducted slice F (cross-cutting). Every structural claim was verified with Trailmark (`QueryEngine.from_directory(..., language="c_sharp")`, proxy-aware `callers_of`) or LSP `incomingCalls`/`outgoingCalls`. The hub independently verified every major finding and the key minor/nit claims by direct code reading + two offline suite runs (Telescope.Tests **288/288**, NeoVisual.Tests **200/200**). No file under `MyExtension/`, `Telescope/`, `tests/`, `tools/` was modified.
- **Prior-fix verification:** all 45 prior findings confirmed fixed — the pane-host collapse (PaneNavigationEngine + TryDispatch deleted, the geometric pipeline collapsed into `FocusTargetModel`), the fzf hardening (kill-before-write, `_probed`/`_value` split, timeout CTS, QuoteArg), the overlay correctness fixes (ApplyPreviewCaret clamp, RenderedTextLength re-pin, CancelPendingG, ResultMapper Ordinal), the shared `FileContentCache`, the WindowManager/navigation cleanup, the vim-mode named tokens, the harness gates (T1/T3/T4/T5/T6), the shared `TestRunner`, and the doc refresh.

## Summary

**34 findings: 0 critical, 4 major, 19 minor, 11 nit.** The post-fix code is in strong shape — the 45-fix plan landed cleanly, the pure-state-machine seams are single-sourced, logging is centralized, net472 compliance is clean, and both offline suites pass (288 + 200). The sharp edges are: (1) **two real UI-thread/resource hazards in the query-driven finders** — the Grep scan still runs inline on the UI thread (the D5 "off-thread" fix only made `ScanFile` pure; the caller loop was deliberately reverted to inline) and the Fzf gather passes `CancellationToken.None`, so a stale gather spawns uncancellable fzf subprocesses; (2) **a silent, asymmetric routing bug** — `Shift+` simple shortcuts (documented in the config contract) can never fire because the hook pre-filter blocks shift-only chords; (3) **stale test counts in three source-of-truth docs** (268/191 vs the verified 288/200); and (4) a cluster of small correctness/perf residuals (a static Error-List cache that goes stale on build, an unhooked COM event subscription, a block-caret focus-regain gap, a double visual-tree walk per tool-window key, and several harness assertions that cannot fail).

## Findings table

| id | sev | file:line | problem |
|----|-----|-----------|---------|
| M1 | major | `Telescope/Finders/GrepFinder.cs:120-131,148-182` | D5 "off-thread" fix incomplete: the per-file scan + the open-time warm-up still run inline on the UI thread (the comment admits the Task.Run hop was dropped) |
| M2 | major | `Telescope/Finders/FzfFinder.cs:145` | Query-driven gather passes `CancellationToken.None` — a stale/closed gather keeps spawning fzf subprocesses that are never killed |
| M3 | major | `MyExtension/Input/InputHandler.cs:473` vs `KeybindingConfig.cs:156,166` / `KeyNameBuilder.cs:7,28` | `IsKeyOfInterest` returns false for shift-only chords, so a user-configured `Shift+...` simple shortcut can never fire (silent, asymmetric vs Ctrl+/Alt+) |
| M4 | major | `AGENTS.md:107,124`; `docs/spec.md:342,363,573-574`; `SKILL.md:299-300` | Stale test counts 268/191 vs the verified 288/200 in three source-of-truth docs |
| m1 | minor | `MyExtension/Package/Utils/ErrorListGatherer.cs:33-37` | Static mutable cache (2s TTL) keyed on (severity, file) never invalidated on build/edit — a `],e` press within 2s of a build returns stale entries |
| m2 | minor | `MyExtension/Package/Utils/RecentFilesGatherer.cs:146-147` + `MyExtensionPackage.cs:544-557` | `_documentEvents` COM connection point never unhooked; the gatherer is not disposed on package unload |
| m3 | minor | `MyExtension/Input/InputHandler.cs:453` | `IsKeyOfInterest` does not short-circuit on `_telescope.IsOpen` — the overlay-open hot path still runs the sentinel re-stat + controller resolution per key |
| m4 | minor | `MyExtension/Adornments/BlockCaretAdornment.cs:102-108` | `OnLostFocus` deactivates the block caret but nothing re-activates it on focus regain (the comment claims a path that does not exist) |
| m5 | minor | `MyExtension/ToolWindows/SolutionExplorerController.cs:207` + `TextMotionHelper.cs:77` | `TryMove` runs `FindFocusedTextBox()` twice per routed key (two visual-tree walks) |
| m6 | minor | `Telescope/Overlay/Utils/FocusTargetModel.cs:272` | The collapsed `SelectTarget` still mirrors `WindowNavigationEngine.SelectTarget` (D1 residual — the two pipelines can drift) |
| m7 | minor | `Telescope/Finders/GrepFinder.cs:114-119,166-171` | The solution-name invalidation block is duplicated verbatim in `GetCandidates` and `WarmContentCache` |
| m8 | minor | `Telescope/Controller/TelescopeController.cs:35` + `GrepFinder.cs:46`, `FzfFinder.cs:54`, `CodeIssuesFinder.cs:52` | The D7 shared-cache collapse is convention-dependent — every finder ctor defaults to `new FileContentCache(500)`, so a finder registered without injection silently reverts |
| m9 | minor | `Telescope/Finders/Utils/FileContentCache.cs:82-106` | `EvictIfNeeded` is O(n) per insert → O(n²) at the Grep open-time warm-up |
| m10 | minor | `Telescope/Finders/Utils/FileContentCache.cs:19-24,42-73` | The shared cache is not thread-safe, yet `GrepFinder.ScanFile`'s doc claims it can run on a background task — a latent race the moment M1 is fixed |
| m11 | minor | `Telescope/Filter/FzfFilter.cs:209-238` | Timeout-vs-completion race: `Task.WhenAny` can return `timeout` in the same instant `all` completes → spurious timeout + unfiltered list |
| m12 | minor | `Telescope/Overlay/TelescopeOverlay.cs:1206-1212` | `OnPaneClicked` → `FocusPane` never clears `_gPending` — a `g` in one pane then a click then `g` in another fires `gg` |
| m13 | minor | `tools/harness/test-e2e.ps1:1411,1699,1706,1763,1944,2381` | Five scenarios still read the whole log with `Get-Content`, bypassing the T5 LogCache tail-read |
| m14 | minor | `tools/harness/test-e2e.ps1:887,895,903,911` | The four severity-nav outcome assertions accept the no-op form — a navigator that always no-ops passes |
| m15 | minor | `tools/harness/test-e2e.ps1:1440` | `preview tokens=\d+` is presence-only — accepts `tokens=0`, cannot fail |
| m16 | minor | `tools/harness/test-e2e.ps1:833` | `neovisual-window-management` step 4 (tool-window `w,d`) asserts only the binding, never the close outcome |
| m17 | minor | `tests/NeoVisual.Tests/Program.cs:606-645` | `Run_WindowManager_DefaultControllerCache_ReturnsCachedInstance` reaches into private implementation via reflection |
| m18 | minor | `docs/progress.md:257-258` | Baseline section still says 268/191 while the same file's Done entry records 288/200 |
| m19 | minor | `docs/reviews/code-review.md:16,49` + `tools/lint/check-doc-refs.ps1:48-56` | The report references deleted files (PaneNavigationEngine.cs, TryDispatch.cs) and is not in the lint doc set |
| n1 | nit | `MyExtension/Navigation/WindowNavigator.cs:26-30` | Static mutable linked-cache fields retain COM RCWs across navigations (the A6 copy fix is present; the static state remains) |
| n2 | nit | `MyExtension/Navigation/WindowNavigator.cs:53` | Ctor resolves `VsServices.Dte(package)` unconditionally but only uses it in the `currentFrame == null` fallback |
| n3 | nit | `MyExtension/Input/InputHandler.cs:379,403` | `TryRouteToolWindowKey` computes the tool-window routing decision twice per key (two formulations of the same decision) |
| n4 | nit | `MyExtension/Package/Utils/PreviewEditorHost.cs:99` | `File.GetLastWriteTimeUtc` is a filesystem stat on every `Show` (every preview selection move), even on the cache-hit path |
| n5 | nit | `Telescope/Logging/Utils/FilterFailureLog.cs:14` | Two `[Telescope]`-prefix helpers with different API shapes (`FilterFailureLog.Format` vs `TelescopeLog.Log`) |
| n6 | nit | `Telescope/Overlay/Utils/Panes/PaneSelectionSync.cs:18` | `Steps(from, to) => to - from` — a one-line subtraction wrapped in a dedicated class + pinned test |
| n7 | nit | `Telescope/Filter/FzfFilter.cs:95-102` | `IsAvailableAsync` has no interlock — two concurrent callers both run the bounded probe |
| n8 | nit | `tests/NeoVisual.Tests/Program.cs:276` | `Assert.False(cfg.Bindings["g,b"] == ...)` workaround for the missing `Assert.NotEqual` |
| n9 | nit | `tests/TestRunner.cs:57-76` | No per-test timeout — a deadlocking test hangs the whole suite |
| n10 | nit | `tools/harness/test-e2e.ps1:1845-1866` | `telescope-goto` Part 1's bounded re-walk retry silently masks the first-attempt 0-gather race |
| n11 | nit | `tools/harness/test-e2e.ps1:1352-1353` | `explorer-open-searchbox` uses a fixed 500ms sleep (sleepy-test smell) |

## Detailed findings

### M1 (major) — the Grep scan still runs inline on the UI thread (D5 residual)

- **Where:** `Telescope/Finders/GrepFinder.cs:120-131` (the per-file scan loop in `GetCandidates`), `:148-182` (`WarmContentCache`), with the UI-thread assert at `:107`.
- **What's wrong:** The 45-fix made `ScanFile` `internal static` (pure, off-thread-capable — the seam exists), but the caller loop was deliberately reverted to inline: the comment at `:120-122` says "the synchronous blocking is unchanged — drop the wasted Task.Run thread hop and scan inline on the UI thread." `GetCandidates` runs the full-solution `LiteralLineScanner` scan on the UI thread per debounced keystroke, and `WarmContentCache` (`:148-182`) reads **every** project file synchronously on the UI thread at overlay open. Two independent workers (arch-auditor D + code-review-worker D) reported this independently.
- **Why it bites:** On a real (large) solution the Grep finder freezes the overlay on every debounced keystroke and for the whole open-time warm-up — hundreds of ms of unresponsive UI, keystrokes queueing behind the blocked thread. The e2e scratch solution is too small to catch it, so this ships green.
- **Fix:** Run the scan loop in `Task.Run` (the pure `ScanFile` seam already exists) and marshal the hit list back with the existing `_queryGeneration` check; keep only the DTE enumeration on the UI thread. **Precondition:** make `FileContentCache` thread-safe first (m10).

### M2 (major) — the Fzf query-driven gather has no cancellation

- **Where:** `Telescope/Finders/FzfFinder.cs:145` — `await _fzf.FilterAsync(lines, query, CancellationToken.None)`.
- **What's wrong:** The overlay's query-driven path (`RefreshQueryDrivenAsync`) has no cancellation token, and `FzfFinder.GetCandidatesAsync` passes `CancellationToken.None` to the fzf engine. The `_queryGeneration` check discards stale results but cannot abort the in-flight gather.
- **Why it bites:** When the user types a new query or closes the overlay mid-gather, the previous gather keeps spawning one fzf subprocess per file (up to HitCap=200) that is never killed — the cea9798 fzf hardening (kill-before-write, cancellation CTS) is bypassed on this path. Fast typing on a large solution produces overlapping, uncancellable subprocess storms and wasted work.
- **Fix:** Thread a per-generation CTS (or the overlay's filter CTS) into `GetCandidatesAsync`/`FilterAsync` so a stale or closed gather cancels the running fzf processes.

### M3 (major) — `Shift+` simple shortcuts can never fire

- **Where:** `MyExtension/Input/InputHandler.cs:473` (`if (IsLeaderActive || ctrl || alt) return true;`) vs the config contract at `MyExtension/Input/Utils/KeybindingConfig.cs:156,166` (`IsSimpleShortcut` classifies `Shift+` prefixes) and `MyExtension/Input/Utils/KeyNameBuilder.cs:7,28` (documents `Shift+F4`).
- **What's wrong:** `IsKeyOfInterest` (the hook pre-filter's only gate — LSP-verified) returns false for any shift-only chord (no ctrl/alt, no leader, no tool-window action key). The comment at `:468-472` documents this as an intentional hot-path choice ("every uppercase letter typed in the editor would otherwise run the full HandleKey path"), but it makes the documented `Shift+` simple-shortcut contract silently dead: the key passes through to VS and the action never executes. Ctrl+ and Alt+ shortcuts work — a silent, asymmetric break.
- **Why it bites:** A user who adds e.g. `"Shift+F4": "command:..."` to `%APPDATA%\MyExtension\keybindings.json` gets a binding that never fires, with no diagnostic and no e2e coverage. The config contract and `KeyNameBuilder` docs promise it works.
- **Fix:** In `IsKeyOfInterest`, also return true when `shift` is held and the built `KeyNameBuilder.Build(key, ctrl, shift, alt)` name exists in the simple bindings (or pass the simple-binding key set into the pre-filter), so bound Shift+ chords reach `HandleKey` while unbound uppercase letters stay cheap.

### M4 (major) — stale test counts in three source-of-truth docs

- **Where:** `AGENTS.md:107,124` ("268 tests"/"191 tests"), `docs/spec.md:342,363,573-574`, `.opencode/skills/vs-extension-dev/SKILL.md:299-300`.
- **What's wrong:** All three docs claim Telescope.Tests **268** / NeoVisual.Tests **191**. The hub verified the actual counts by running both suites: **288 passed, 0 failed** and **200 passed, 0 failed**. `docs/progress.md:392-393` already records the 45-findings plan as 268→288 / 191→200.
- **Why it bites:** A verifier trusting AGENTS.md expects 268/191 and can misjudge a regression as a pass (or vice versa); the docs are the source of truth the loop reads before ordering work.
- **Fix:** Update all sites to 288/200.

### m1 (minor) — ErrorListGatherer static cache goes stale on build

- **Where:** `MyExtension/Package/Utils/ErrorListGatherer.cs:33-37` (static `Clock`, `_cacheKey`, `_cacheStampMs`, `_cache`), `:44-49` (2s TTL check).
- **What's wrong:** The A3 fix introduced a static mutable cache with a 2s TTL keyed only on (severity, filePath), never invalidated on a build or edit event. The class doc at `:29-32` acknowledges ErrorItems has no version counter and chose a TTL instead.
- **Why it bites:** A build completing within 2s of a `],e`/`],w` press returns the stale entry list — the user navigates to a line whose error/warning is already gone (or misses a new one). The static state is the R40 class the repo explicitly avoids and makes hermetic tests order-dependent if they exercise `Gather`.
- **Fix:** Invalidate on `SolutionEvents` build-done / `DocumentEvents` text change, or key the cache on the document's edit version; at minimum make it instance-scoped.

### m2 (minor) — RecentFilesGatherer event-subscription leak

- **Where:** `MyExtension/Package/Utils/RecentFilesGatherer.cs:146-147` (`_documentEvents = dte.Events.DocumentEvents; _documentEvents.DocumentOpened += OnDocumentOpened;`) + `MyExtension/Package/MyExtensionPackage.cs:544-557` (`Dispose` disposes `_keyboardHook`/`_windowManager`/`_telescope` but not `_recentFilesGatherer`).
- **What's wrong:** The COM connection point is held (correctly, to keep the subscription alive) but never unhooked, and the gatherer has no `Dispose`/`Unhook`.
- **Why it bites:** On package unload/reload (VSIX update, disable/enable) the `DocumentOpened` subscription leaks and keeps firing into a torn-down gatherer — the event-subscription-leak class the seed checklist flags.
- **Fix:** Make `RecentFilesGatherer` `IDisposable`, unhook `DocumentOpened` + null `_documentEvents`, and dispose it in the package `Dispose`.

### m3 (minor) — `IsKeyOfInterest` does not short-circuit while the overlay is open

- **Where:** `MyExtension/Input/InputHandler.cs:453` (the pre-filter), with `_telescope.IsOpen` already checked at `:290` inside `HandleKey`.
- **What's wrong:** While the modal overlay is open, every interesting key (Ctrl chords, leader Space, Escape, tool-window action keys) still runs the sentinel re-stat (every 250ms), the `CurrentController` resolution, and marshals to `HandleKey`, which immediately returns false at `:290` (the overlay owns all keys).
- **Why it bites:** This is exactly the per-key hot path the pre-filter exists to keep cheap; the wasted marshal + dictionary lookups add up over a long overlay session.
- **Fix:** Return false at the top of `IsKeyOfInterest` when `_telescope.IsOpen`.

### m4 (minor) — block caret never re-activates on focus regain

- **Where:** `MyExtension/Adornments/BlockCaretAdornment.cs:102-108` (`OnLostFocus` sets `Active = false`; the comment at `:105-106` claims "ApplyEditorViewCaret re-activates it when the view regains focus in normal mode").
- **What's wrong:** There is no `GotAggregateFocus` handler, and `ApplyEditorViewCaret` (grep-verified) is only called from `TextMotionHelper.StyleFocusedSurface` (mode change) and `ApplyMotionToBox` (motion) — never on focus regain.
- **Why it bites:** After a Command Window / Immediate Window editor view loses and regains focus in normal mode, the block caret stays off (native line caret) until the user toggles mode or moves — the `block-caret active=True` diagnostic and the vim-style caret are both wrong until then.
- **Fix:** Subscribe `GotAggregateFocus` in the adornment (or have `TextInputToolWindowController`/`TextMotionHelper` re-apply `ApplyEditorViewCaret(view, !isInputMode)` on focus gain), mirroring the `LostAggregateFocus` handler.

### m5 (minor) — double visual-tree walk per tool-window key

- **Where:** `MyExtension/ToolWindows/SolutionExplorerController.cs:207` (`if (TextMotionHelper.FindFocusedTextBox() != null)`) + `MyExtension/ToolWindows/Utils/TextMotionHelper.cs:77` (`TryMoveFocusedSurface(key, out enteredInputMode, FindFocusedTextBox())`).
- **What's wrong:** `TryMove` calls `FindFocusedTextBox()` as its gate, then `TextMotion(key)()` → `TryMoveFocusedSurface` calls it again — two visual-tree walks per routed key, on every hjkl/action key even when the tree (not the search box) is focused.
- **Why it bites:** This is the per-key cost the M1/C3 fix (cached `_focusedTextBoxInCurrentToolWindow` in `WindowManager`) was meant to eliminate, and it bypasses the R17 "resolve the box once" optimization that the `focusedBox` overload exists for.
- **Fix:** Resolve the box once (or use `_windowManager.IsFocusedTextBoxInCurrentToolWindow()`), pass it into `TextMotion(key, box)`/`TryMoveFocusedSurface(key, out _, box)`, and skip the gate walk when the cached fact says no box is focused.

### m6 (minor) — `FocusTargetModel.SelectTarget` still mirrors the window engine (D1 residual)

- **Where:** `Telescope/Overlay/Utils/FocusTargetModel.cs:272` (the collapsed geometric pipeline), with the two pinned deviations documented at `:243-252`.
- **What's wrong:** The 45-fix collapsed `PaneNavigationEngine` into `FocusTargetModel`, but the pipeline itself still re-implements `WindowNavigationEngine.SelectTarget` (in-direction → aligned → closest gap → largest adjacency → last-tie) over `PaneRect` analogues.
- **Why it bites:** A future bug fix to the window engine's geometric selection (divide tolerance, tie-break rule, a new filter predicate) silently leaves the pane-focus pipeline divergent — the exact drift class that produces "works in window nav, broken in overlay focus" bugs.
- **Fix:** Extract the geometric selection (rect + direction → target) into one shared pure class both assemblies reference, or replace it with a ~10-line direction→target table for the fixed 3-pane layout.

### m7 (minor) — duplicated solution-invalidation block in GrepFinder

- **Where:** `Telescope/Finders/GrepFinder.cs:114-119` (`GetCandidates`) and `:166-171` (`WarmContentCache`) — the `_cachedSolutionName` compare + `_fileCache.Invalidate()` block is verbatim in both.
- **Why it bites:** A fix to one copy (e.g. a null-solution edge) is easily missed in the other, and the two paths can disagree on cache validity.
- **Fix:** Extract a private `EnsureSolutionCache(dte)` helper.

### m8 (minor) — the D7 shared-cache collapse is convention-dependent

- **Where:** `Telescope/Controller/TelescopeController.cs:35` + `GrepFinder.cs:46`, `FzfFinder.cs:54`, `CodeIssuesFinder.cs:52` — every finder ctor defaults `contentCache ?? new FileContentCache(500)`.
- **What's wrong:** The shared-cache injection works only because the controller happens to pass the shared instance; a finder registered without it silently reverts to its own 500-entry cache with no compile error.
- **Why it bites:** A future finder added without the shared-cache injection quietly regresses the D7 collapse (per-finder caches, duplicated disk reads) with no signal.
- **Fix:** Make the cache a required ctor param (drop the default) or have the controller construct/register finders so the shared instance is guaranteed.

### m9 (minor) — `FileContentCache.EvictIfNeeded` is O(n) per insert

- **Where:** `Telescope/Finders/Utils/FileContentCache.cs:82-106` (linear scan for the oldest entry on every insert past the cap).
- **Why it bites:** The Grep open-time warm-up inserts up to N entries → O(n²) at overlay open (500 entries × full solution), compounding M1's UI-thread stall.
- **Fix:** Use a LinkedList+Dictionary LRU or a coarse clock-based eviction.

### m10 (minor) — `FileContentCache` is not thread-safe (latent)

- **Where:** `Telescope/Finders/Utils/FileContentCache.cs:19-24,42-73` (plain `Dictionary` + `_accessCounter`, no lock), while `GrepFinder.ScanFile`'s doc (`:221-224`) claims it "can run on a background task".
- **Why it bites:** Currently UI-thread-only so no active race — but the moment M1's off-thread fix is re-applied, a background thread mutating `_entries`/`_accessCounter` concurrently with the UI thread corrupts the Dictionary (lost entries / infinite loop in `EvictIfNeeded`).
- **Fix:** Add a lock around `GetLines`/`GetContent`/`EvictIfNeeded` (or make the cache thread-safe) before wiring any off-thread caller.

### m11 (minor) — fzf timeout-vs-completion race

- **Where:** `Telescope/Filter/FzfFilter.cs:209-238` (`Task.WhenAny(all, timeout)`).
- **What's wrong:** `WhenAny` can return `timeout` in the same instant `all` completes; the code then kills the process and returns the unfiltered `lines` even though the filter actually produced output.
- **Why it bites:** A filter that completes exactly at the 3s boundary returns the full unfiltered list and logs a spurious `fzf filter failed: timeout after 3000ms` — a wrong result + a misleading contract line.
- **Fix:** After the `winner == timeout` branch, re-check `all.IsCompleted` (or `outputTask.IsCompleted`) before returning `lines`; if completed, fall through to the fast path.

### m12 (minor) — `_gPending` leaks across a left-click focus change

- **Where:** `Telescope/Overlay/TelescopeOverlay.cs:1206-1212` (`OnPaneClicked` → `FocusPane`), vs `CancelPendingG` only called at `:1093` (prompt-motion path) and inside `OverlayKeyHandler.HandleNormal`.
- **What's wrong:** A lone `g` sets `_gPending`; a left-click focus change never clears it, so `g` in the List pane → click the prompt → `g` in the prompt fires `gg` (MoveToFirst) from a stale pending-g.
- **Why it bites:** A cross-pane state leak — a focus change should reset the pending-gg chord, else a stray `g` from another pane triggers an unexpected jump.
- **Fix:** Clear `_gPending` (call `_keyHandler.CancelPendingG()`) in `FocusPane`/`ApplyFocusTarget` on any focus change.

### m13 (minor) — five scenarios bypass the LogCache tail-read

- **Where:** `tools/harness/test-e2e.ps1:1411,1699,1706,1763,1944,2381` (telescope-wrap/references/implementation/goto/recent).
- **What's wrong:** These scenarios still read the whole log with `Get-Content` to extract candidate counts, bypassing the T5 LogCache tail-read that `telescope-navigate:630-631` was fixed to use.
- **Why it bites:** The T5 fix is applied inconsistently — O(n) whole-file reads per scenario, and the stated discipline ("never a whole-log Get-Content") is violated.
- **Fix:** Replace each `Get-Content` + last-match loop with `Update-LogCache` + a `$script:LogCache` scan.

### m14 (minor) — severity-nav outcome assertions accept the no-op form

- **Where:** `tools/harness/test-e2e.ps1:887,895,903,911`.
- **What's wrong:** The four `],e`/`[,e`/`],w`/`[,w` assertions accept `diagnostic-nav (direction=... severity=... |no-op: )` — a navigator that ALWAYS no-ops (e.g. a broken severity filter that never matches) passes.
- **Why it bites:** The T1 outcome gate is weakened for these four: only the `leader-binding executed:` line proves the binding fired; the navigator's actual behavior is unasserted.
- **Fix:** Seed a file with a deterministic warning/error and assert the target form (`direction=… severity=… target=…`), or assert the no-op only when the Error List is provably empty.

### m15 (minor) — `preview tokens=\d+` is presence-only

- **Where:** `tools/harness/test-e2e.ps1:1440`.
- **What's wrong:** The assertion accepts `tokens=0`, so it cannot fail and cannot discriminate whether the classifier engaged. AGENTS.md itself documents the count reads 0 for both buffer sources.
- **Why it bites:** The scenario gives false confidence that syntax highlighting works — a cannot-fail assertion.
- **Fix:** Drop the assertion or gate it on a non-zero count once the workspace-attach fix lands; until then mark it explicitly as a known-limitation smoke check, not a highlighting proof.

### m16 (minor) — tool-window `w,d` close outcome unasserted

- **Where:** `tools/harness/test-e2e.ps1:833`.
- **What's wrong:** `neovisual-window-management` step 4 (tool-window `w,d`) asserts only `leader-binding executed: w,d` — the close outcome is not asserted (the editor-focused `w,d` at `:810-817` does poll the active document).
- **Why it bites:** A regression where close-window fires the binding but fails to close the tool window passes.
- **Fix:** Assert the outcome via DTE (poll that the Solution Explorer tool window is no longer visible) or add a window-state diagnostic.

### m17 (minor) — reflection-coupled test

- **Where:** `tests/NeoVisual.Tests/Program.cs:606-645` (`Run_WindowManager_DefaultControllerCache_ReturnsCachedInstance`).
- **What's wrong:** The test reaches into private implementation — reflection into the `ThreadHelper.uiThreadDispatcher` static field plus invoking private `GetController`.
- **Why it bites:** Renaming the field/method breaks the test even when behavior is preserved; mutating a static field is a shared-state risk (mitigated by the N50 save/restore, but the coupling remains).
- **Fix:** Prefer a hermetic seam (internal `GetController` visible via `InternalsVisibleTo`, or a constructor-injected cache) over reflection.

### m18 (minor) — progress.md Baseline contradicts its own Done entry

- **Where:** `docs/progress.md:257-258` (Baseline: 268/191 "after Gap 4") vs `:392-393` (45-findings entry: 268→288 / 191→200 GREEN).
- **Why it bites:** The hub reads progress.md as the single source of truth; an internally contradictory Baseline misdirects the next item's verification.
- **Fix:** Refresh the Baseline unit counts to 288/200.

### m19 (minor) — the live report references deleted files and is unguarded

- **Where:** `docs/reviews/code-review.md:16,49` (D1 → `PaneNavigationEngine.cs:78-204`, D13 → `TryDispatch.cs:1` — both deleted by the pane-host collapse) + `tools/lint/check-doc-refs.ps1:48-56` (code-review.md is not in the default doc set).
- **Why it bites:** A reader of the "live" report sees file:line refs to files that no longer exist, and the mechanical gate cannot catch future drift in this report (the W21 gap was closed for architecture-review.md but not for code-review.md).
- **Fix:** Add code-review.md to the lint doc set (or annotate the D1/D13 refs as pre-collapse).

### n1–n11 (nit) — see the findings table. Highlights: the static linked-cache COM retention in `WindowNavigator` (n1, the A6 copy fix is present — the residual is the static-mutable state); the unconditional ctor DTE resolution (n2); the double tool-window routing decision in `TryRouteToolWindowKey` (n3); the per-`Show` filesystem stat in `PreviewEditorHost` (n4); the two `[Telescope]`-prefix helpers (n5); the over-engineered `PaneSelectionSync.Steps` (n6); the un-interlocked `IsAvailableAsync` probe (n7); the `Assert.NotEqual` workaround (n8); the missing per-test timeout in the shared runner (n9); the silent first-attempt mask in `telescope-goto`'s retry (n10); and the fixed 500ms sleep in `explorer-open-searchbox` (n11).

## Cross-cutting (slice F — hub-conducted)

- **Duplication between slices:** the big clusters stay resolved (`TextMotionDispatcher` single dispatch, `BlockCaretStyle` single caret renderer, `KeyToArrowVk` single-sourced, `GetAsyncKeyState` declared once, the retired `SyntaxHighlighter` fully gone, the shared `TestRunner`). Remaining: the mirrored `FocusTargetModel.SelectTarget` vs `WindowNavigationEngine.SelectTarget` (m6), the duplicated GrepFinder invalidation block (m7), the convention-dependent shared cache (m8), the `SolutionExplorerController` double walk (m5), and the static `WindowNavigator` cache (n1).
- **net472/BCL consistency:** clean — no `IReadOnlySet<T>`/modern-BCL usage anywhere (all workers verified).
- **Namespace/folder hygiene:** clean post-restructure.
- **Log-format drift across the two projects:** the remaining drift points are the two `[Telescope]`-prefix helpers (n5) and the spurious `fzf filter failed: timeout` line on the M11 race; the `[Telescope]`/`[NeoVisual]`/`[Hook]`/`[MyExtension]` prefix contract is otherwise centralized in `DiagnosticLog`.
- **Hook-path cost:** `InputHandler.HandleKey` (complexity 13) + the overlay-open non-short-circuit (m3) + the double tool-window routing decision (n3) + the double `FindFocusedTextBox` walk (m5) are the remaining hot-path concerns; the per-key `[Hook]` log stays fixed.

## Verified-clean (checked this run, no action)

- **The 45 prior findings are all fixed** — verified by direct code reading (pane-host collapse, fzf kill-before-write, `_probed`/`_value` split, timeout CTS, QuoteArg, ApplyPreviewCaret clamp, RenderedTextLength, CancelPendingG, ResultMapper Ordinal, shared FileContentCache, eager-loop deletion, per-controller FocusKeeper, VimBufferSubscriptions, named vim-mode tokens, harness gates T1/T3/T4/T5/T6, shared TestRunner).
- **Vim-motion dispatch is single-sourced** through `TextMotionDispatcher` (LSP-verified: `TextMotionHelper.MapMotion` → `TextMotionDispatcher.MapKey`).
- **Logging is centralized** — the only `Debug.WriteLine` in Telescope is the opt-in path inside `NeoVisualLog`.
- **Both offline suites pass** — Telescope.Tests **288/288**, NeoVisual.Tests **200/200** (hub-verified by running them).
- **Doc-reference lint PASS** (0 unresolved backticked refs) and **doc-content lint PASS** (12/12) — the mechanical gates are green; the drift is content-level (M4, m18, m19).
- **net472 compliance:** no modern-BCL APIs anywhere in `MyExtension/` or `Telescope/`.

## Recommendations (ordered by effort/impact)

1. **Fix the query-driven UI freeze + subprocess storm (M1 + M2 + m10)** — run the Grep scan off-thread (make `FileContentCache` thread-safe first), and thread a per-generation CTS into the Fzf gather. Small-to-medium; removes the two biggest hazards in the modular core.
2. **Fix the `Shift+` shortcut dead-binding (M3)** — return true in `IsKeyOfInterest` for bound Shift+ chords. Small; closes a silent, asymmetric contract break.
3. **Refresh the stale test counts (M4, m18)** — update AGENTS.md/spec.md/SKILL.md/progress.md to 288/200. Trivial doc edit.
4. **Fix the Error-List cache staleness (m1)** and the RecentFilesGatherer leak (m2). Small correctness fixes.
5. **Close the harness cannot-fail assertions (m13–m16)** — LogCache tail-reads, severity-nav outcome assertions, the tokens presence-only line, the w,d close outcome. Small harness changes.
6. **Fix the block-caret focus-regain gap (m4)** and the double visual-tree walk (m5). Small.
7. **Resolve the mirrored pane pipeline (m6)** — extract the shared geometric selection or replace with a direction table. Medium, the biggest architecture win remaining.
8. **Harden the shared cache (m8, m9)** and the fzf timeout race (m11). Small.
9. **Clean up the nits (n1–n11)** opportunistically.

## Filed into progress.md

**Nothing filed** — the user declined the Step-4 filing (2026-10-05, via the `question` tool). The report lives in `docs/reviews/code-review.md` only. Cross-references to `docs/reviews/architecture-review.md` and the prior `docs/reviews/code-review.md`: M1 is the residual of the prior D5 (the seam was made pure but the caller loop was reverted to inline); M2 is a new angle on the fzf hardening (the kill-before-write fix is present but the query-driven path bypasses it with `CancellationToken.None`); m6 is the residual of the prior D1 (the mirrored pipeline survives inside `FocusTargetModel`); m8 is the residual of the prior D7 (the shared cache is convention-dependent); m10 is a precondition for M1's fix.
