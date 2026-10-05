# MyExtension — Code Review (code-review-hub)

- **Date:** 2026-10-05
- **Scope:** whole repo — `MyExtension/` (core + `Navigation/` + `ToolWindows/` + `Vim/`), `Telescope/`, `tests/`, `tools/`, and the workflow docs. **Primary focus: the new modular telescope architecture** (slice D — the `Telescope/` project: the pane host, the finder architecture, the fzf filter, the overlay wiring) — most resources were spent there.
- **Method:** 1 whole-repo `trailmark-recon` digest (2508 nodes, 982 proxies = 39.2%, 0 entrypoints, 100 high-blast-radius, 28 complexity hotspots) shared with every worker + 6 parallel workers (2 deep on slice D — `arch-auditor` + `code-review-worker`; 1 `arch-auditor` + 1 `code-review-worker` on slices A–C; 1 `test-quality-reviewer` on slice E; 1 `docs-accuracy-reviewer` on the docs) + hub-conducted slice F (cross-cutting). Every structural claim was verified with Trailmark (`QueryEngine.from_directory(..., language="c_sharp")`, proxy-aware `callers_of`) or LSP `incomingCalls`/`outgoingCalls`. The hub independently verified the highest-impact claims by direct code reading (PaneNavigationEngine mirror, FzfFilter kill-on-cancel ordering, RefreshQueryDrivenAsync UI-thread gather, ApplyPreviewCaret clamp, OverlayKeyHandler `_gPending`, WindowManager dead lazy cache, PreviewEditorHost full-buffer GetText, ErrorListGatherer O(n), neovisual-window-nav outcome gate, vim-mode contract, pane untestedness, per-finder cache instances). No file under `MyExtension/`, `Telescope/`, `tests/`, `tools/` was modified.
- **User context:** the `.md` docs are KNOWN-STALE (the previous session's changes were not pushed) — **the code is the source of truth**. Every claim was verified against the actual source, not the docs. Docs-accuracy findings are therefore reported as informational (they will be resolved when the docs are refreshed).

## Summary

**45 findings: 0 critical, 9 major, 24 minor, 12 nit.** The modular telescope architecture is genuinely well-built at the seams the repo already invested in — the pure state machines (`OverlayKeyHandler`, `TextMotionNavigator`, `TextMotionDispatcher`, `FocusTargetModel`, `PaneNavigationEngine`) are dependency-free and unit-tested, vim-motion dispatch is single-sourced through `TextMotionDispatcher`, the retired `SyntaxHighlighter` is fully gone, logging is centralized, and net472 compliance is clean. The sharp edges are: (1) the **pane host is over-engineered for 3 fixed panes** — `PaneNavigationEngine` is a ~200-line mirrored re-implementation of `WindowNavigationEngine` with exactly one production caller, and `FocusTargetModel` + `PaneHost` duplicate the focused-pane state; (2) **three real UI-thread/perf hazards in the modular core** — the query-driven finders gather inline on the UI thread (overlay freeze on large solutions), `PreviewEditorHost.Show` materializes the whole preview buffer per selection move, and the fzf kill-on-cancel registration is ordered after the blocking stdin write (a hung fzf leaks a process and strands the filter task); (3) a **false-positive e2e gate** on the core navigation feature (`neovisual-window-nav` never asserts the navigation outcome); and (4) a **dead production path** in `WindowManager` (the R20 lazy default-controller cache is shadowed by an eager registration loop that was never removed).

## Findings table

| id | sev | file:line | problem |
|----|-----|-----------|---------|
| D1 | major | `Telescope/Overlay/Utils/Panes/PaneNavigationEngine.cs:78-204` | ~200-line mirrored re-implementation of `WindowNavigationEngine` + `WindowRect` (header admits "MIRRORED, not referenced"); one production caller |
| D2 | major | `Telescope/Overlay/Utils/Panes/*.cs` + `FocusTargetModel.cs` | Modular pane host over-engineered for 3 fixed panes (8 files, two state machines, a mirrored engine) |
| D3 | major | `Telescope/Filter/FzfFilter.cs:153-177` | Kill-on-cancel registration is AFTER the blocking stdin write — a hung fzf leaks a process and strands the filter task |
| D4 | major | `Telescope/Overlay/TelescopeOverlay.cs:1119-1122` | `ApplyPreviewCaret` passes the raw caret with no clamp (ShowPreview clamps via `PreviewCaretMap.Offset`) → out-of-range SnapshotPoint on a stale buffer |
| D5 | major | `Telescope/Overlay/TelescopeOverlay.cs:504-516` + `GrepFinder.cs:107-110` | Query-driven gather runs inline on the UI thread → overlay freeze on large solutions |
| A1 | major | `MyExtension/ToolWindows/WindowManager.cs:31` + `MyExtensionPackage.cs:143-150` | R20 lazy `_defaultControllers` cache is dead in production — the eager registration loop was never removed |
| A2 | major | `MyExtension/Package/Utils/PreviewEditorHost.cs:100` | `Show` materializes the whole preview buffer via `CurrentSnapshot.GetText()` on every call, even on mtime-cache hit |
| A3 | major | `MyExtension/Package/Utils/ErrorListGatherer.cs:45-46` | `],e`/`[,e`/`],w`/`[,w` re-enumerate the ENTIRE Error List (O(n) COM reads) on every invocation |
| T1 | major | `tools/harness/test-e2e.ps1:698-709` | `neovisual-window-nav` asserts only "fired" diagnostics, never the navigation OUTCOME (`navigate activated index=` / absence of `navigate no-op:`) |
| D6 | minor | `FocusTargetModel.cs:71` + `PaneHost.cs:19` | Duplicated focused-pane state (`FocusTargetModel.Current` vs `PaneHost._active`) kept in sync only by overlay call sites |
| D7 | minor | `CodeIssuesFinder.cs:40`, `GrepFinder.cs:28,49`, `FzfFinder.cs:33,57` | `ProjectFileCache`/`FileContentCache` instantiated per-finder → DTE tree walked up to 2× per TTL, file contents cached up to 3× independently |
| D8 | minor | `Telescope/Filter/FzfFilter.cs:118-226` | Per-keystroke subprocess spawn (class doc admits it); hand-rolled `QuoteArg` reinvents Windows argv quoting |
| D9 | minor | `Telescope/Overlay/Utils/ResultsFormatter.cs:24-39` | `RenderedTextLength` exists solely to keep a legacy diagnostic byte-identical to the RETIRED TextBox render |
| D10 | minor | `Telescope/Overlay/Utils/OverlayKeyHandler.cs:156-164` + `TelescopeOverlay.cs:1031` | Lone `g` sets `_gPending`; prompt motions consumed before `_keyHandler.Handle` never clear it → `g h g` triggers `gg` |
| D11 | minor | `Telescope/Filter/FzfFilter.cs:77-85` | `_availability` written from a thread-pool continuation, read on the UI thread, no `volatile` |
| D12 | minor | `Telescope/Overlay/Utils/ResultMapper.cs:34` | byDisplay map groups with `OrdinalIgnoreCase` → case-colliding duplicates map to the wrong payload |
| A4 | minor | `MyExtension/ToolWindows/SolutionExplorerController.cs:193` | `TryMove` re-implements the text-input routing block `ToolWindowControllerBase.TextMotion` already provides |
| A5 | minor | `MyExtension/ToolWindows/Utils/TextMotionHelper.cs:107` | WPF path reads `focusedBox.Text` (full-buffer copy) before slicing — the R18 "avoids the O(n) copy" claim is only half-realized |
| A6 | minor | `MyExtension/Navigation/WindowNavigator.cs:26` + `BuildActiveWindows:107` | Static `_cachedLinked` keyed on object refs, returns the SAME list instance to every navigator (R40 class) |
| A7 | minor | `MyExtension/ToolWindows/GeneralToolWindowController.cs:81` | `IsTextInputType` hardcoded switch must be manually kept in sync with the enum + GUID map |
| A8 | minor | `MyExtension/Input/InputHandler.cs:267` | `HandleKey` ~20-branch hot path with a 6-arg `ShouldRouteToolWindowKey` call |
| A9 | minor | `MyExtension/Package/Utils/RecentFilesGatherer.cs:158` | `_sessionMru` `List.Remove`+`Insert(0,…)` per open — O(n) each, unbounded |
| C1 | minor | `MyExtension/Vim/VimModeTracker.cs:138` + `VimModeClassifier.cs:27` | `vim-mode=Unknown` + numeric `vim-mode=<n>` (Visual/Command) outside the documented `Insert|Normal|Replace` contract |
| C2 | minor | `MyExtension/ToolWindows/Utils/FocusKeeper.cs:17` | `_current` static `DispatcherTimer` shared across controllers; `Run` stops any prior keeper regardless of owner |
| C3 | minor | `MyExtension/Input/InputHandler.cs:338` | `IsFocusedTextBoxInCurrentToolWindow()` (COM + visual-tree walk) runs on every shift+key routed to a tool window |
| C4 | minor | `MyExtension/Hooks/Utils/InjectedKeyGuard.cs:83` | `TryConsume` keyed by VK+TTL only — cannot distinguish injected from physical; a dropped injected event consumes the next physical same-VK |
| C5 | minor | `MyExtension/Vim/VimModeTracker.cs:39` | `IsEditorFocused` tracks `[ContentType("text")]` editable views only — a non-text editor fails `FocusGuard` OPEN |
| T2 | minor | `tests/Telescope.Tests/Program.cs` | The three concrete panes (`PromptPane`/`ListPane`/`PreviewPane`) are completely untested (0 references) |
| T3 | minor | `tools/harness/harness-common.ps1:555-562` | `Assert-NoSeedLeak` "skips gracefully" when the expected tree is absent — a cannot-fail path in the write-leak guard |
| DOC1 | minor | `AGENTS.md:145-146` + `SKILL.md` + `spec.md` | "44 registered — 43 executed GREEN; telescope-recent pending" stale vs progress.md's 44/44 GREEN (informational — known-stale docs) |
| DOC2 | minor | `docs/spec.md:390` | Claims 44 scenarios but the list has 43 — `neovisual-git-bindings` missing (informational) |
| DOC3 | minor | `docs/progress.md:381-388` | Pending-queue run-order block still shows gap 11/feature 7/gap 4 pending though all GREEN (informational) |
| DOC4 | minor | `docs/reviews/code-review.md:70-71` + `architecture-review.md:46` | Prior-review findings N54/N55/W12 presented as open but resolved (informational) |
| D13 | nit | `Telescope/Overlay/Utils/TryDispatch.cs:1` | Comment-only stub file retained as a "seam marker" after the merge into `TextMotionDispatcher` |
| D14 | nit | `FocusTargetModel.cs:29-37,129-140` | `PaneFocusKey` + `MapKey` duplicate the Ctrl+H/J/K/L chord mapping |
| D15 | nit | `Telescope/Filter/FzfFilter.cs:180` | Timeout `Task.Delay` never cancelled/disposed on the fast path — per-keystroke pending timer |
| A10 | nit | `MyExtension/ToolWindows/WindowManager.cs:127` | "Walk up to DocView content" loop duplicated in `ComputeTextInputSurfaceFocused` + `IsFocusedTextBoxInCurrentToolWindow` |
| A11 | nit | `MyExtension/Input/InputHandler.cs:453` | `IsKeyOfInterest` resolves `_windowManager.CurrentController` twice per key-down |
| C6 | nit | `MyExtension/ToolWindows/SolutionExplorerController.cs:31` | `_focusKeeper` retains a disposed handle, never reset to null |
| C7 | nit | `MyExtension/ToolWindows/WindowManager.cs:341` | `GetGuidProperty` HRESULT discarded → silent `_type = Unknown` on COM failure |
| T4 | nit | `tools/harness/harness-common.ps1:168-190` | `Wait-LogLine`'s `$searchedTo` is a local cursor re-initialized per call → O(calls × window) scans |
| T5 | nit | `tools/harness/test-e2e.ps1:624` | `telescope-navigate` reads the whole log with `Get-Content`, bypassing the LogCache tail-read machinery |
| T6 | nit | `tools/harness/test-e2e.ps1:53-66` | Suite order-dependency invariants (seed-leak last, editor-insert before it) not enforced by the runner |
| DOC5 | nit | `docs/reviews/code-review.md:191` | Test counts 153/163 stale vs current 268/191 (informational) |
| DOC6 | nit | `docs/spec.md:470-555` | §7 done-feature list omits the git leader bindings (informational) |

## Detailed findings

### D1 (major) — `PaneNavigationEngine` is a mirrored re-implementation of the window engine

- **Where:** `Telescope/Overlay/Utils/Panes/PaneNavigationEngine.cs:78-204` (plus `PaneRect`/`PaneDirection`/`PaneAxis`/`GapTo`/`Adjacency`/`IsInDirection`/`IsAligned` at :7-75).
- **What's wrong:** The file's own header (:78-89) admits it: "The pane analogue of `MyExtension.Navigation.WindowNavigationEngine` (the SAME pipeline over pane rects)… MIRRORED, not referenced: Telescope.csproj does not reference MyExtension." It re-implements the whole geometric selection pipeline (in-direction → aligned → closest gap → largest adjacency → last-in-list tie-break) plus a `WindowRect` analogue, with two documented "pinned deviations" (the `gap >= 0` filter and the dropped DPI divide). Trailmark `callers_of('proxy.unresolved:PaneNavigationEngine.SelectTarget')` returns exactly ONE production caller (`FocusTargetModel.Move`) + 7 unit tests.
- **Why it bites:** Any future change to the window engine's algorithm (divide tolerance, tie-break rule, a new filter predicate, DPI handling) silently diverges from the pane engine — the two "pinned deviations" are documented but the core is duplicated, so a fix applied to one surface will not propagate to the other. This is the exact drift class that produces "works in window nav, broken in overlay focus" bugs.
- **Fix:** Extract the geometric selection into a shared dependency-free library both `MyExtension` and `Telescope` reference, parameterized by the rect/direction types; or, for the fixed 3-pane layout, replace the engine with a ~10-line direction→target table and delete the mirrored pipeline.

### D2 (major) — the modular pane host is over-engineered for 3 fixed panes

- **Where:** `Telescope/Overlay/Utils/Panes/*.cs` (8 files: `IPane`, `PaneHost`, `PaneNavigationEngine`, `PromptPane`, `ListPane`, `PreviewPane`, `PaneSelectionSync`) + `FocusTargetModel.cs`.
- **What's wrong:** The geometric engine has exactly one production caller (D1), and the layout is fixed at design time (Input bottom, List left, Preview right — `PaneHost.cs:13`). The "reusable core for the deferred lazygit overlay" claim is only half-justified: `IPane` + `PaneHost` (registry, activation, click normalization) are genuinely reusable, but `PaneNavigationEngine` + `FocusTargetModel` are Telescope-specific geometric machinery that a lazygit overlay with a different pane layout would have to re-derive anyway.
- **Why it bites:** The abstraction cost is paid now (8 files, two state machines, a mirrored navigation engine) for a deferred feature that may never arrive or may need a different shape; every future pane change must thread through the model/host/engine split, and the geometric engine's existence invites "fix the engine" work instead of the trivial table the fixed layout needs.
- **Fix:** Keep `IPane`/`PaneHost` as the reusable contract; collapse `FocusTargetModel` + `PaneNavigationEngine` into a single small pure focus resolver (or a direction table) and delete the mirrored engine; defer further generalization until the lazygit overlay is actually planned.

### D3 (major) — fzf kill-on-cancel is registered after the blocking stdin write

- **Where:** `Telescope/Filter/FzfFilter.cs:153-177`.
- **What's wrong:** The cancellation registration (`cancellationToken.Register(() => TryKill(p))` at :177) is only active *after* `await Task.Run(...)` finishes writing the candidate list to stdin (:153-169). If fzf hangs while reading stdin, the `BaseStream.Write` blocks once the ~64KB pipe buffer fills (large candidate lists), the token cancellation cannot kill the process, the `Task.Run` never completes, and the `using var p` (:140) never disposes — a leaked hung fzf process per keystroke that neither the timeout (`Task.WhenAny` is only reached after the write) nor overlay close can reclaim.
- **Why it bites:** A hung fzf on a large solution leaves orphaned processes and a permanently-pending `FilterAndUpdateAsync` task — the overlay's filter path silently wedges.
- **Fix:** Register the kill callback on the token *before* the spawn/write (or move the write into the same `using` scope as the registration) so cancellation can kill the process during the write phase.

### D4 (major) — `ApplyPreviewCaret` passes the raw caret with no clamp

- **Where:** `Telescope/Overlay/TelescopeOverlay.cs:1119-1122`.
- **What's wrong:** `ApplyPreviewCaret` passes the raw `_previewNavigator.Caret` to `_previewEditor.ApplyCaret` with no clamp, while `ShowPreview` (line 880) clamps via `PreviewCaretMap.Offset(result.Text, ...)` as the mtime-drift guard.
- **Why it bites:** If the previewed file changes on disk while the overlay is open (the preview uses the LIVE workspace buffer for open files, so the buffer can shrink under the stale navigator text), a preview motion key (h/l/j/k/w/b/e/0/$/gg/G) hands an out-of-range index to the editor's `SnapshotPoint` → exception thrown from `OnPreviewKeyDown` (uncaught).
- **Fix:** Clamp in `ApplyPreviewCaret` the same way `ShowPreview` does (`PreviewCaretMap.Offset` against the current editor text).

### D5 (major) — query-driven gather runs inline on the UI thread

- **Where:** `Telescope/Overlay/TelescopeOverlay.cs:504-516` + `GrepFinder.cs:107-110` (and `FzfFinder`).
- **What's wrong:** `RefreshQueryDrivenAsync` awaits `finder.GetCandidatesAsync(query)` on the UI thread (the WPF SynchronizationContext is captured after the debounce — the comment at :501-502 confirms it), and `GrepFinder`/`FzfFinder` run the full-solution scan inline on the UI thread (confirmed `GrepFinder.cs:107-110`; `GrepFinder.GetCandidates` is a complexity-8 hotspot).
- **Why it bites:** On a large solution the overlay freezes for the duration of each post-debounce scan (hundreds of ms of unresponsive UI; keystrokes queue behind the blocked thread), and the `_queryGeneration` check can't help because the scan itself blocks.
- **Fix:** Run the gather on a background task (`Task.Run`) and marshal the result back to the UI thread with the existing generation check.

### A1 (major) — the R20 lazy default-controller cache is dead in production

- **Where:** `MyExtension/ToolWindows/WindowManager.cs:31` + `MyExtension/Package/MyExtensionPackage.cs:143-150`.
- **What's wrong:** The package's "controllers" init step eagerly registers `WindowManager.DefaultControllerFor(type)` for every non-null `ToolWindowType` (:143-150), so `GetController`'s registered-lookup always hits and the R20 lazy `_defaultControllers` cache (:31) never fires. Two mechanisms for the same per-type controller; the R20 comment claims the lazy cache removes the need for eager registration, but the eager loop was never removed.
- **Why it bites:** A future change to one path (e.g. adding a type to `DefaultControllerFor`) silently diverges from the other; the dead cache is untested production code that misleads readers into thinking per-type defaults are lazy.
- **Fix:** Delete the eager registration loop and rely on the lazy `_defaultControllers` cache (or delete the cache and keep eager); keep exactly one mechanism.

### A2 (major) — `PreviewEditorHost.Show` materializes the whole preview buffer per selection move

- **Where:** `MyExtension/Package/Utils/PreviewEditorHost.cs:100`.
- **What's wrong:** `Show` returns `_view!.TextBuffer.CurrentSnapshot.GetText()` on every call, even when the mtime cache skips a rebuild (:94-97) — so every preview selection change copies the whole file into a new string on the UI thread.
- **Why it bites:** Previewing a large file (10k+ lines) allocates a full-buffer string per selection move in the overlay, causing UI-thread GC pressure and jank while navigating results.
- **Fix:** Return the snapshot (or a lazy text accessor) instead of the materialized string, or cache the text keyed by snapshot version and only re-read on rebuild.

### A3 (major) — diagnostic-nav re-enumerates the entire Error List per press

- **Where:** `MyExtension/Package/Utils/ErrorListGatherer.cs:45-46`.
- **What's wrong:** `],e`/`[,e`/`],w`/`[,w` re-enumerate the ENTIRE Error List on every invocation (O(n) COM `ErrorItems.Item(i)` reads on the UI thread), then filter to one file + one severity (`InputHandler.cs:601`).
- **Why it bites:** With a solution holding thousands of errors, each diagnostic-nav press is a full Error List scan on the UI thread — the dominant cost of a frequently-used navigation action, and it grows with solution size.
- **Fix:** Cache the gather per (file, severity) keyed on the Error List's version/count, or use the Error List's own filtering; at minimum bound the scan and reuse across consecutive presses.

### T1 (major) — `neovisual-window-nav` never asserts the navigation outcome

- **Where:** `tools/harness/test-e2e.ps1:698-709`.
- **What's wrong:** The scenario asserts only the "fired" diagnostics (`shortcut-binding executed: Ctrl+H` + `navigate direction=L`) and never the navigation OUTCOME — it never asserts `navigate activated index=` nor the ABSENCE of `navigate no-op: <reason>` (the m47 outcome diagnostic exists precisely to distinguish fired-vs-noop).
- **Why it bites:** This is the primary e2e coverage for the core Cardinal-navigation feature, yet a regression that makes every navigation a no-op (e.g. all candidate windows filtered out by the `!IsEmpty`/`IsInDirection` pipeline) or that navigates to the wrong window still passes: `navigate direction=L` is logged when the action runs, before the outcome is known.
- **Fix:** Assert the outcome contract per direction: `Assert-NewLogLine ... 'navigate activated index=\d+'` AND assert `navigate no-op:` does NOT appear (mirroring how `telescope-focus-panes` pins the no-op edges).

### D6–D15, A4–A9, C1–C7, T2–T3, DOC1–DOC4 (minor) — see the findings table; highlights:

- **D6** — `FocusTargetModel.Current` (decision state) and `PaneHost._active` (WPF activation state) both track the focused pane, kept in sync only by the overlay's call sites — a desync would log `focus target=X` while pane Y holds focus, breaking the `[Telescope] focus target=` contract and `telescope-focus-panes`. Make `PaneHost` the single owner.
- **D7** — `ProjectFileCache`/`FileContentCache` are instantiated per-finder (Grep + Fzf + Issues each own one), so the same DTE tree is walked up to 2× per TTL and the same file contents cached up to 3× independently — the caches are the intended mitigation but fragmented across instances. Share one of each across all finders.
- **D8** — per-keystroke fzf subprocess spawn (the class doc admits "we spawn a short-lived process per query"); the hand-rolled `QuoteArg` (N45/BP-59) is a reinvented Windows argv quoter. Switch to fzf `--listen` or an in-process matcher.
- **D9** — `RenderedTextLength` exists solely to keep a legacy diagnostic byte-identical to the RETIRED TextBox render ("the legacy 2-char selection-marker allowance", "the legacy '\n' separator") — the `results count=... boxText=L` contract is pinned to dead layout math. Re-pin to a meaningful value.
- **D10** — a lone `g` sets `_gPending`; prompt motions (h/l/w/b/e/0/$) are consumed by `TryPromptMotion` before `_keyHandler.Handle`, so they never clear it — `g h g` triggers `gg` (MoveToFirst). Clear `_gPending` in `TryPromptMotion`.
- **D11** — `_availability` is written from a thread-pool continuation (`ConfigureAwait(false)`) and read on the UI thread with no `volatile` — a latent cross-thread race (a stale `null` spawns fzf once even after the probe cached false).
- **D12** — `ResultMapper` groups byDisplay with `OrdinalIgnoreCase`, so "Foo.cs"/"foo.cs" share a bucket and a case-colliding duplicate can map to the wrong payload (wrong file opened on Enter). Use `Ordinal`.
- **A4** — `SolutionExplorerController.TryMove` re-implements the text-input routing block `ToolWindowControllerBase.TextMotion` already provides — the search-box motion path is the same logic written twice.
- **A5** — the WPF path reads `focusedBox.Text` (a full-buffer string copy) before slicing; the R18 comment claims the slice avoids "the O(n) GetText() copy", but `.Text` IS that copy — only the `LineIndex` build is bounded by `MotionSliceRadius`. Correct the comment or read a bounded window.
- **A6** — `_cachedLinked`/`_cachedLinkedSource`/`_cachedLinkedActive` are static mutable fields keyed on object references (the R40 class); `BuildActiveWindows:107` returns `_cachedLinked` directly — the SAME list instance to every navigator. Move to the `WindowManager` instance or return a copy.
- **A7** — `GeneralToolWindowController.IsTextInputType` is a hardcoded switch that must be manually kept in sync with the `ToolWindowType` enum + `ToolWindowTypeResolver` GUID map — a new text-input type silently starts in normal mode (hjkl inject arrows).
- **A8** — `HandleKey` is the recon-flagged ~20-branch hot path with a 6-arg `ShouldRouteToolWindowKey` call — extract the tool-window routing block.
- **A9** — `_sessionMru` uses `List.Remove` + `Insert(0, ...)` per `DocumentOpened` — O(n) each, unbounded. Use a `LinkedList` or cap it.
- **C1** — `vim-mode=Unknown` (on every editor→tool-window focus loss) and numeric `vim-mode=<n>` (Visual=4, Command=3 via `VimModeClassifier.cs:27`) are outside the documented `vim-mode=Insert|Normal|Replace` contract — a strict/negative harness assertion over the mode line would break.
- **C2** — `FocusKeeper._current` is a static `DispatcherTimer` shared across controllers; `Run` stops any prior keeper regardless of owner (latent — only SolutionExplorerController uses it today).
- **C3** — `IsFocusedTextBoxInCurrentToolWindow()` (COM `GetProperty(VSFPROPID_DocView)` + visual-tree walk) runs on every shift+key routed to a tool window — cache the fact on focus-change events (the M1 pattern).
- **C4** — `InjectedKeyGuard.TryConsume` is keyed by VK+TTL only and cannot distinguish our injected event from a physical one — a dropped injected event would consume the next physical same-VK key-down within 1s (theoretical; `keybd_event` queues synchronously).
- **C5** — `IsEditorFocused` tracks `[ContentType("text")]` editable views only; a non-text editor (designer, .resx, binary) never sets it, so with a stale `IsToolWindow` the `FocusGuard` fails OPEN and action keys leak into the editor — the exact leak the guard was built to prevent (narrow, but fail-open).
- **T2** — the three concrete panes (`PromptPane`/`ListPane`/`PreviewPane`) are completely untested (0 references in `tests/Telescope.Tests`), while `IPane`/`PaneHost`/`PaneNavigationEngine`/`FocusTargetModel`/`PaneSelectionSync` are all covered — a regression in any pane's `Activate`/`Deactivate` or content wiring is invisible to the suite.
- **T3** — `Assert-NoSeedLeak` "skips gracefully" when the expected-result tree or scratch dir is absent — a cannot-fail path in the suite's only write-leak guard. Throw in a full (non-reuse) run when the expected tree is missing.
- **DOC1–DOC4** — known-stale docs (user: not pushed): the "43 executed GREEN / telescope-recent pending" claim, the 43-vs-44 scenario list, the pending-queue run-order block, and the prior-review "still open" annotations. Informational — resolved when the docs are refreshed.

### D13–D15, A10–A11, C6–C7, T4–T6, DOC5–DOC6 (nit) — see the findings table. Highlights: the comment-only `TryDispatch.cs` stub (D13); the duplicated Ctrl-chord mapping in `PaneFocusKey`/`MapKey` (D14); the never-cancelled timeout `Task.Delay` on the fzf fast path (D15); the duplicated DocView walk-up loop (A10); the double `CurrentController` resolution per key-down (A11); the retained disposed `_focusKeeper` handle (C6); the discarded `GetGuidProperty` HRESULT (C7); the per-call `$searchedTo` cursor making wait helpers O(calls × window) (T4); the `Get-Content` whole-log read in `telescope-navigate` (T5); the unenforced suite order-dependency invariants (T6); stale test counts + missing git-bindings bullet in the docs (DOC5/DOC6).

## Cross-cutting (slice F — hub-conducted)

- **Duplication between slices:** the big clusters are resolved (`TextMotionDispatcher` single dispatch, `BlockCaretStyle` single caret renderer, `KeyToArrowVk` single-sourced, `GetAsyncKeyState` declared once, the retired `SyntaxHighlighter` fully gone). Remaining: the mirrored `PaneNavigationEngine` vs `WindowNavigationEngine` (D1), the duplicated focused-pane state (D6), the per-finder cache instances (D7), the `SolutionExplorerController.TryMove` vs `ToolWindowControllerBase.TextMotion` routing block (A4), the `WindowManager` DocView walk-up loop (A10), and the `WindowNavigator` static cache (A6).
- **net472/BCL consistency:** clean — no `IReadOnlySet<T>`/modern-BCL usage anywhere (all workers verified).
- **Namespace/folder hygiene:** clean post-restructure.
- **Log-format drift across the two projects:** the remaining drift points are `vim-mode=Unknown`/numeric (C1), the legacy `boxText=L` length math (D9), and the `_availability` race's redundant `fzf filter failed` log (D11); the `[Telescope]`/`[NeoVisual]`/`[Hook]`/`[MyExtension]` prefix contract is otherwise centralized in `DiagnosticLog`.
- **Hook-path cost:** `InputHandler.HandleKey` (complexity 20) + the double `CurrentController` resolution (A11) + the per-shift-key COM walk (C3) are the remaining hot-path concerns; the per-key `[Hook]` log stays fixed.

## Verified-clean (checked this run, no action)

- **Vim-motion dispatch is single-sourced:** LSP `incomingCalls` on `TextMotionDispatcher.Handle` shows `TelescopeOverlay.TryPromptMotion` + `HandlePreviewKey`, and Trailmark shows ToolWindows' `TextMotionHelper.MapMotion` delegates to it — the motion math lives in the shared `TextMotionNavigator`.
- **The retired `SyntaxHighlighter` is gone** (Trailmark node scan: 0 matches).
- **Logging is centralized** — the only `Debug.WriteLine` in Telescope is the opt-in path inside `NeoVisualLog`; no bypasses.
- **`FileContentCache` vs `ProjectFileCache` are NOT near-duplicates** (mtime+LRU vs TTL+single-list) — the prior "three caches" premise is really one cache class with two accessors plus one TTL cache (the residual is the per-finder *instances*, D7).
- **Doc-reference lint PASS** (0 unresolved backticked refs across 28 docs) and **doc-content lint PASS** (12/12) — the mechanical gates are green.
- **net472 compliance:** no modern-BCL APIs anywhere in `MyExtension/` or `Telescope/`.

## Recommendations (ordered by effort/impact)

1. **Fix the fzf kill-on-cancel ordering (D3)** — register the kill callback before the spawn/write. Small, closes a real resource-leak + wedged-filter bug in the modular core.
2. **Fix the query-driven UI freeze (D5)** — run the gather on a background task. Small, removes the overlay freeze on large solutions.
3. **Fix the `neovisual-window-nav` outcome gate (T1)** — assert `navigate activated index=` + absence of `navigate no-op:`. Small harness change, closes a false-positive gate on the core feature.
4. **Fix the `ApplyPreviewCaret` clamp (D4)** — clamp like `ShowPreview`. Small, closes an uncaught-exception edge case.
5. **Resolve the pane-host over-engineering (D1/D2/D6)** — collapse `FocusTargetModel` + `PaneNavigationEngine` into one small pure focus resolver (or a direction table), make `PaneHost` the single owner of the active pane, and delete the mirrored engine. Medium, the biggest architecture win in the modular core.
6. **Fix the dead `WindowManager` lazy cache (A1)** — delete the eager registration loop or the cache; keep one mechanism. Small.
7. **Fix the preview-buffer materialization (A2)** and the Error List O(n) scan (A3). Small-to-medium perf wins on frequently-used paths.
8. **Share the per-finder caches (D7)** — construct one `ProjectFileCache` + one `FileContentCache` in `TelescopeController` and inject. Small.
9. **Fix the `_gPending` vim-state deviation (D10)** and the `ResultMapper` case-collision (D12). Small correctness fixes.
10. **Harden the seed-leak guard (T3)** and add pane tests (T2). Small-to-medium test-quality wins.
11. **Reconcile the docs (DOC1–DOC6)** when the previous session's changes are pushed — the mechanical lints are green; the drift is content-level.

## Filed into progress.md

**Nothing filed yet** — pending the user's Step-4 selection (via the `question` tool). Cross-references to `docs/reviews/architecture-review.md` and the prior `docs/reviews/code-review.md`: D8/D11 are the residual of the prior N38/N39 fzf findings (the `_availability == false` check is now present; the residual is the cross-thread race + the per-keystroke spawn); D7 is a new angle on the prior N33 cache finding (the cache classes are fine; the per-finder instances are the issue); A5 is the residual of the prior R18/N19 slice finding (the WPF path still copies the whole buffer); A6 is the residual of the prior N11 `_cachedLinked` finding (still static, still keyed on object refs).
