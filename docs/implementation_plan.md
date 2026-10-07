# Plan — Code review fixes (107 findings, incl. nits) + Telescope generalization

> **Lane: feature (unit-only, e2e DEFERRED).** The e2e scenarios are QUEUED in
> `e2e-queue.md` (status QUEUED) — created/executed on a capable machine after this
> plan is GREEN (the user's 2026-10-06 instruction: "we are not on e2e capable machine
> so defer those to queue for later"). RED is proven at the unit level only. M-M7: this
> plan has a small set of diagnostic-BEHAVIOR changes (M3 fzf failure, M1 sentinel
> active, m15 select-file, m16 keeper timing, m18 text-motion caret, m28 List-pane
> shift, m30 Ctrl guard, m43 sanitized filter-failed, m46 boxText, m9 no spurious
> cancel log) — each is flagged with its harness site; no NEW harness-asserted
> diagnostic literals (two harness-INDEPENDENT failure-path literals are flagged:
> m17's `[NeoVisual] selection events advise failed: 0x{hr:X8}` and m32's
> `[Telescope] preview failed: {msg}`).
>
> **Source:** `docs/reviews/code-review.md` (2026-10-07 refresh). The review's summary
> says "107 findings: 0 critical, 6 major, 79 minor, 22 nit", but its OWN findings table
> lists **107 rows (6 M + 76 m + 25 n)** — the total is consistent (107) but the
> minor/nit split differs (79/22 vs 76/25). This plan covers **ALL 107 table rows**
> (the user asked for "everything that code review found"). The summary split is a
> doc-drift item (fixed in the Phase 8 doc refresh).
>
> **User's second request (folded in):** "check if telescope can be make even more
> general/simplified and put that into plan also." A dedicated Telescope
> generalization deep-dive + web research (Telescope.nvim architecture) concluded the
> DO-IT items are already covered by the review findings (m33 → QueryDrivenFinderBase,
> m41 → shared column builders, m34/m35/m36/m39/m40/m42/m47/m48 → dead-code deletion);
> the DEFER/DON'T analysis is documented in the "Telescope generalization analysis"
> section below.
>
> **Research (4 subagents, 2026-10-07):** `feature-researcher` (web — Telescope.nvim
> picker decomposition, LazyVim migrated to snacks.nvim, native VS search NOT
> extensible to custom finders, VS Code QuickPick pattern, build-light recommendation);
> `trailmark-recon` (structural — graph 2922 nodes / 1186 proxies = 40.6%; all 6 major
> findings CONFIRMED; 1 correction — M3's whitespace fallback is FzfFilter.cs:175-178,
> not 308/329; blast-radius map: TelescopeOverlay out=140/22 HBR, InputHandler 54,
> SolutionExplorerController 47, PreviewEditorHost 46, TextMotionHelper 37, FzfFilter
> 32; Telescope structure: 8 finders — 6× FinderBase<THit> + 2× SymbolFinderBase — 6
> hit classes : FileLocation, 3 panes, FzfHit/ImplementationHit are type aliases);
> `arch-auditor` (change-area — all 8 clusters confirmed, sequencing hazards below);
> `arch-auditor` (Telescope generalization deep-dive — DO IT / DEFER / DON'T list).
>
> **Key research corrections baked in:**
> - **M3 is sound but the FileFinder path must keep the full-list return.** The
>   graceful-degradation contract (`FilterAsync` returns the unfiltered list) is
>   CORRECT for the FileFinder path (TelescopeOverlay.cs:537 → `MapBack`); the
>   whitespace short-circuit (FzfFilter.cs:175-178) is also correct for FileFinder.
>   Only the TIMEOUT (:308) and CRASH (:329) paths may signal failure distinctly
>   (return `null` / throw a typed exception); `FzfFinder` treats that as
>   empty-or-literal-fallback, and its empty-query check (:90) becomes
>   `IsNullOrWhiteSpace`. A whitespace query then shows 0 hits instead of garbage.
> - **M4 is sound but the sync `GetCandidates` throw breaks ~15 GrepFinder tests.**
>   The tests at Program.cs:3164-3344 call the sync `GetCandidates("q")` — the
>   "throw for non-empty" fix must be PAIRED with rewriting those tests to use
>   `GetCandidatesAsync` (or the hermetic seam) in the same step.
> - **m1 needs a test BEFORE the fix.** The OnBufferClosed-then-Detach double-decrement
>   is UNTESTED — the plan adds `Run_VimBufferSubscriptions_NoDoubleDecrement` first.
> - **m14's Esc case is already handled** (ExitInputMode → OnModeChanged → :95); only
>   the CLICK case is the gap — the `_cachedFocusedBox` invalidation must key on focus
>   change, not just mode change.
> - **m16 must not break `explorer-open-navigation`.** The FocusKeeper stop-on-editor-
>   focus change touches the same keeper `SelectFirstSourceFile` uses; the e2e gate
>   (E2E-CR107-5) verifies.
> - **m10's dead mirrors are pinned by tests.** `WindowRect.GapTo`/`Adjacency` are
>   pinned by NeoVisual 2363/2371; `PaneRect` mirrors by the FocusTargetModel tests;
>   `DirectionExtensions.ToChar` IS pinned (live diagnostic) — the deletion must be
>   paired with the pinned-test updates.
> - **m46's missing absorber is a rendering change.** The Files/Recent catalogs get an
>   `int.MaxValue` absorber → the assigned widths change → `results count=... boxText=`
>   may change (flagged for the `telescope-results-columns` gate).
> - **The count discrepancy.** The summary's 79/22 split vs the table's 76/25 — the
>   total is 107 either way; the Phase 8 doc refresh fixes the summary split.

## Goal

Fix ALL 107 code-review findings (incl. nits) from `docs/reviews/code-review.md`
(2026-10-07 refresh): the fzf garbage-results bug (M3), the query-driven finder
UI-thread residuals (M4), the incomplete M6 navigation isolation (M2), the never-armed
stale-toolwindow sentinel (M1), the flaky test (M5), the architecture-contract doc
drift (M6), the 76 minor findings (m1-m76), and the 25 nits (n1-n25) — plus the
Telescope generalization work the user requested (QueryDrivenFinderBase, shared column
builders, dead-code deletion; the DEFER/DON'T analysis documented) — with every fix
RED-proven at the unit level and the e2e scenarios QUEUED (deferred to a capable
machine).

## Approach (phases)

**Phase 0 — Query-driven finder core (M3, M4, m9, m33, m34, m38, n13, n14, n15).**
Make `FzfFilter` signal failure distinctly on timeout/crash (return `null` / throw a
typed exception) while KEEPING the full-list return for the FileFinder path + the
whitespace short-circuit (M3); make `FzfFinder` treat failure as empty-or-literal-
fallback and its empty-query check `IsNullOrWhiteSpace` (M3); move the whole gather
(read + `BuildCandidates` + `FilterAsync` + `MapBatched` + the literal scan) inside the
`Task.Run` (M4); make `GrepFinder`'s sync `GetCandidates` throw for a non-empty query
(M4 — PAIRED with rewriting the ~15 tests that call it); make `CodeIssuesFinder`
`GatherHits` async (M4); suppress the spurious cancelled-gather failure line (m9);
extract the shared `QueryDrivenFinderBase` (m33 — the generalization win: ~100
duplicated lines across Grep/Fzf collapse into one base with a matcher hook); delete
the dead `FzfLineMapper.Map` (m34); fix the `FileContentCache` TOCTOU (m38); drop the
redundant `lines.ToArray()` (n13); remove the `_fileCache!.Get` null-forgiving (n14);
delete the never-emitted `DiagnosticLog.GlobalKeyboard` constant (n15).

**Phase 1 — Navigation isolation (M2, m10, m11, m12, m13, n4).**
Wrap `CompareWindows` in `LinkedTo`'s `Where` clause + `FindActive`'s `FirstOrDefault`
predicate in the same per-window try/catch `GetLinkedWindowsList` uses (M2 — closes the
"one stale frame kills all navigation" residual); delete the dead `WindowRect`/
`PaneRect` `GapTo`/`Adjacency` mirrors of the shared `GeometricSelectionEngine` (m10 —
PAIRED with the pinned-test updates); drop the unused `caption` parameter of
`MatchesPropertiesQuirk` (m11); replace the `(int)direction` coupling with an explicit
mapping (m12); handle the legitimately-null `_activeWindow` path (m13); delete the
redundant `ExtractFrames` wrapper (n4).

**Phase 2 — Vim interop (m1, m6).**
Fix the `VimBufferSubscriptions.Detach` double-decrement (m1 — ADD the
OnBufferClosed-then-Detach test FIRST); make `VimModeSource.Detach`→`UnsubscribeBuffer`
read the cached text buffer first (mirror the m13-fixed `OnBufferClosed` path) instead
of re-resolving via reflection on a possibly-closing buffer (m6).

**Phase 3 — Hook + input + package (M1, m2, m3, m4, m5, m7, m8, n1, n2, n3).**
Arm the stale-toolwindow sentinel in production (M1 — `_sentinelArmed =
StaleToolWindowSentinel.Path != null` at construction, or a production-reachable
refresh gated on the harness env var, so `neovisual-explorer-move-editor-focus`
actually exercises the FocusGuard leak guard); single-source the physical-modifier-VK
set (m2 — the two lists have already drifted); resolve `CurrentController` once in
`ExitToolWindowInputMode` (m3); stop consuming every modifier key-down as "transparent"
while a leader sequence is pending (m4 — a Ctrl+chord/Alt+Tab in that window must not
have its modifier swallowed); fix the shift-aware `KeyNames.ToString` for non-letter
keys (m5 — a binding like `w,}` must fire); prune the `PreviewEditorHost._textCache`
(m7); invalidate the `ErrorListGatherer` 2s TTL on more events (m8); delete the dead
`GotoDecision` enum (n1); single-source the duplicated kind-mapping expression in
`RoslynGatherers` (n2); collapse the test-only ctor's re-initialized field state (n3).

**Phase 4 — Tool-windows (m14, m15, m16, m17, m18, m19, m20, m21, m22, n5, n6, n7, n8).**
Invalidate `_cachedFocusedBox` on focus change (m14 — the click case); set
`Expanded = true` before recursing into SolutionFolder nodes in `FindFirstProjectNode`
(m15 — `g` no longer logs `select none` for a project inside a collapsed solution
folder); stop the FocusKeeper when the user moves to the editor (m16 — keys typed in
that window are no longer routed to the tree and lost); check the `AdviseSelectionEvents`
HRESULT (m17 — a failure must not silently leave `_selectionEventsCookie = 0`);
single-source the caret-relative slice computation 3× + fix the 4096-char slice hiding
text (m18); share the FocusKeeper setup between `ReturnFocusToTree` and
`SelectFirstSourceFile` (m19); freeze the `BlockCaretAdornment` brushes + make the
`_block` Grid non-hit-testable + stop the per-keystroke `Update()` recompute (m20);
unify the two controller-resolution paths (m21); wrap `OnWindowFocusChanged`'s
`GetProperty`/`GetGuidProperty` in try/catch (m22); delegate the no-box
`StyleFocusedSurface` overload (n5); merge the two COM/visual-tree walks per focus
change (n6); register J/K/D0/D4 in `TextInputToolWindowController` (n7 — the Command
Window gains 0/$ like the search box); reclassify `ObjectSearchResultsWindow` as
non-text-input (n8).

**Phase 5 — Overlay + columns + finders (m23-m32, m35, m36, m37, m39-m48, n9-n12, n16-n20).**
Reuse the cached `LineIndex` in `PreviewCaretMap` (m23); single-source the
`ListKeyMap`/`MapKey` tables (m24); delete the dead `TextMotionDispatcher.Handle`
(m25); scope `VisibilityByFinder` per-instance (m26); make `ShowOverlayAsync` gather
async (m27); fix the List-pane `a`/`A`/`i`/`I` shift distinction (m28); refresh the
prompt caret style in `EnterInsert` (m29); check Ctrl in `TryPromptMotion`/
`HandlePreviewKey` (m30 — Ctrl+W must not become NextWord); fix the centering math
units (m31); wrap `ShowPreview` in try/catch (m32); delete the dead
`HierarchyWalker.FirstFileEndingWith` (m35); consolidate the `FileFinder` dual test
seam (m36); merge `RecentFileHit` into `FileHit` (m37); delete the dead
`ColumnVisibilityModel.VisibleColumns` (m39) + `ResultColumn.Width`/`WidthChars` (m40);
single-source the `file`/`line`/`text`/`kind` column literals via shared builders
(m41); drop the `ImplMap` defensive entries (m42); sanitize `FilterFailureLog`'s
`ex.Message` (m43); simplify the `FilterFailureLog` prefix seam (m44); make
`FinderColumns.ForFinder` data-driven (m45); add the `int.MaxValue` absorber to the
Files/Recent catalogs (m46); wire or delete the `projectRoot` param (m47); merge the
`FocusTargetModel` parallel lists (m48); avoid the per-move `List<Candidate>` alloc in
`GeometricSelectionEngine` (n9); merge `PromptPane`/`PreviewPane` into a `DelegatePane`
(n10); delete the `FocusTargetModel.MapKey` pass-through (n11); make
`CodeIssuesFinder._fileCache` readonly (n12); simplify the `ResultsFormatter` seam
(n16); fix the `NeoVisualTraceListener.WriteLine`→`Write` double-stamp (n17); drop the
unused `FzfHit` alias (n18); harden `NeoVisualLog.EnsurePane`'s double-checked locking
(n19); fix the `ResultMapper` thread-safety comment (n20).

**Phase 6 — Test infra (M5, m49-m65, n21-n25).**
Drive `Run_FzfFilter_GraceDelayToken` through the `DelayFactory` seam (M5); make
`Run_FzfFilter_NoSpuriousTimeoutOnCancel` deterministic (m49); remove the reflection
from `Run_FileContentCache_ThreadSafe` (m50), `Run_TelescopeController_DisposeClosesOverlay`
(m51), `Run_PaneFailureTracker_Interlocked` (m52); pin the actual expected displays in
`Run_GetCandidates_DefaultQuery_MatchesNoArg` (m53); consolidate
`Run_ResultsFormatter_RenderedTextLength` with the five `_*` tests (m54); split the
eager `Run_TextMotionDispatcher_MotionsMapToNavigator` (m55); add failure messages to
`Run_ResultsColumns_MinMaxWidths` (m56); make `Run_FzfFilter_KillRegisteredBeforeWrite`
deterministic (m57); assert `entered.Wait(5s)` in `Run_FileContentCache_IOOutsideLock`
(m58); add a message parameter to `Assert.Equal`/`Assert.NotEqual` (m59); reduce the
systemic reflection-based testing in NeoVisual.Tests (m60); exercise the malformed-JSON
path in `Run_KeybindingConfig_Merge` (m61); fix the residual `Assert.True(x == y)` (m62);
isolate the `InjectedKeyGuard` static singleton (m63); make `Run_FocusKeeper_TickCancelled`
deterministic (m64); add `Assert.Throws<T>` (m65); make `Run_FzfFilter_TimeoutRace`
deterministic (n21); add the `SetResults` clamp test (n22); fix
`Run_TextInput_KeysIMapsToInsertStart`'s second assertion (n23); strengthen
`Run_TextInput_StartsInInsertMode`'s presence check (n24); make
`Run_FocusKeeper_ResetAfterDispose` actually dispose (n25).

**Phase 7 — Harness (m66-m71).**
Fix the `explorer-open-searchbox` `editor-view-opened` order-dependence (m66); fix the
`neovisual-editor-insert` `editor-view-opened` order-dependence (m67); replace the fixed
bootstrap sleeps in `iterate-telescope.ps1` with polls (m68); re-assert
`leader-binding executed:` on the git-bindings retry (m69); add an explicit empty-box
guard to `explorer-searchbox-motions` (m70); apply baseline discipline to
`iterate-telescope.ps1`'s `mode=insert` assertion (m71).

**Phase 8 — Docs + lint (M6, m72-m76).**
Fix the spec.md §2.5 + SKILL.md geometric-pipeline attribution (M6 — "FocusTargetModel
runs the shared GeometricSelectionEngine, the same engine WindowNavigationEngine
delegates to"); refresh the progress.md baseline 297/212 → 319/236 (m72) + the
In-progress "106-findings is next" → Gap 5 (m73); fix the e2e-queue.md status count
(m74); fix the spec.md + AGENTS.md `preview tokens=` presence-only claims (m75 — m62
dropped that assertion); fix the agent slice-D lists naming the deleted
`SyntaxHighlighter.cs` (m76); fix the code-review.md summary's minor/nit split to match
the table (76/25); fix the 4 pre-existing unresolved doc-ref refs in the review file
(BP-D36 — `DelegatePane`, `COMException`, `SyntaxHighlighter.cs`,
`Down_OnePixelGapAccepted`) so the doc-ref lint PASSes.

## Acceptance criteria

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | The query-driven finders are hazard-free (M3/M4/m9/m33/m34/m38/n13/n14/n15): fzf failure signals empty-or-fallback (never garbage), the gather is fully off-thread, the sync GetCandidates throws, the cancelled gather logs nothing, the QueryDrivenFinderBase is shared, the dead mapper is gone, the cache has no TOCTOU | `grep hits=...` / `fzf hits=...` UNCHANGED (a whitespace query now shows 0 hits — M3); the spurious `GrepFinder failed to enumerate:` no longer fires on cancel (m9) | `Run_FzfFilter_FailureSignalsEmpty`, `Run_FzfFinder_WhitespaceQueryEmpty`, `Run_FzfFinder_GatherOffThread`, `Run_GrepFinder_SyncGetCandidatesThrows`, `Run_CodeIssuesFinder_GatherAsync`, `Run_GrepFinder_NoSpuriousCancelLog`, `Run_QueryDrivenFinderBase_Shared`, `Run_FzfLineMapper_MapDeleted`, `Run_FileContentCache_NoToctou` |
| AC2 | The navigation isolation is complete (M2/m10/m11/m12/m13/n4): one stale frame can't kill navigation, the dead mirrors are gone, the direction mapping is explicit, the null path is handled | `navigate direction=...` / `navigate activated index=...` UNCHANGED | `Run_WindowFrameAdapter_LinkedIsolation`, `Run_WindowRect_DeadMirrorsRemoved`, `Run_Direction_ExplicitMapping`, `Run_WindowNavigator_NullActive`, `Run_ExtractFrames_Removed` |
| AC3 | The Vim interop is safe (m1/m6): no double-decrement, no reflection on a closing buffer | `vim-mode=...` UNCHANGED | `Run_VimBufferSubscriptions_NoDoubleDecrement`, `Run_VimModeSource_DetachCached` |
| AC4 | The hook/input/package nits are cleaned (M1/m2-m5/m7/m8/n1-n3): the sentinel arms in production, the modifier-VK set is single, CurrentController resolves once, the leader modifier consumption is fixed, the shift-aware ToString works, the caches are bounded | `[NeoVisual] stale-toolwindow sentinel active` fires when the harness creates the sentinel file (M1 — RESTORES the e2e gate); `leader-binding executed: ...` UNCHANGED | `Run_InputHandler_SentinelArmedInProduction`, `Run_LeaderSequenceMatcher_SingleModifierVkSet`, `Run_InputHandler_SingleControllerResolution`, `Run_LeaderSequenceMatcher_ModifierChordPassesThrough`, `Run_KeyNames_ShiftAwareNonLetter`, `Run_PreviewEditorHost_CachePruned`, `Run_ErrorListGatherer_TtlInvalidated` |
| AC5 | The tool-window fixes land (m14-m22/n5-n8): the focus cache invalidates on focus change, the SolutionFolder expands, the keeper stops on editor focus, the HRESULT is checked, the slice is single-sourced, the adornment is frozen, the resolution is unified, the COM callback is guarded | `solution-explorer select file=...` may now succeed where it logged `select none` (m15); `text-motion key=... caret=...` may change near the slice end (m18); `block-caret active=` UNCHANGED | `Run_SolutionExplorer_FocusCacheInvalidated`, `Run_SolutionExplorer_SolutionFolderExpanded`, `Run_FocusKeeper_StopsOnEditorFocus`, `Run_WindowManager_AdviseSelectionHresult`, `Run_TextMotionHelper_SliceShared`, `Run_FocusKeeper_SetupShared`, `Run_BlockCaretAdornment_Frozen`, `Run_WindowManager_SingleResolution`, `Run_WindowManager_FocusChangeTryCatch`, `Run_StyleFocusedSurface_Delegates`, `Run_WindowManager_SingleWalk`, `Run_TextInput_KeysJKD0D4`, `Run_ToolWindowTypeResolver_ObjectSearchNotTextInput` |
| AC6 | The overlay/columns/finders are cleaned (m23-m32/m35-m37/m39-m48/n9-n12/n16-n20): the preview reuses the LineIndex, the key tables are single, the dead code is gone, the gather is async, the shift/Ctrl distinctions are fixed, the centering is correct, the columns are data-driven, the absorber exists, the log seams are simplified | `results columns={ids}` / `results count={n} selected={m} boxText={len}` may change (m46 — the absorber); `key=... mode=... handled=...` may change (m28/m30); `[Telescope] filter failed: {msg}` sanitized (m43); `preview caret=... line=...` UNCHANGED | `Run_PreviewCaretMap_ReusesLineIndex`, `Run_ListKeyMap_SingleTable`, `Run_TextMotionDispatcher_HandleRemoved`, `Run_VisibilityByFinder_Scoped`, `Run_ShowOverlayAsync_AsyncGather`, `Run_ListPane_ShiftDistinction`, `Run_EnterInsert_CaretStyle`, `Run_PromptMotion_CtrlGuard`, `Run_OverlayCentering_Units`, `Run_ShowPreview_TryCatch`, `Run_HierarchyWalker_DeadRemoved`, `Run_FileFinder_SingleSeam`, `Run_RecentFileHit_Merged`, `Run_ColumnVisibilityModel_DeadRemoved`, `Run_ResultColumn_DeadRemoved`, `Run_FinderColumns_SharedBuilders`, `Run_KindAbbreviations_NoDefensive`, `Run_FilterFailureLog_Sanitized`, `Run_FilterFailureLog_Simplified`, `Run_FinderColumns_DataDriven`, `Run_ColumnWidths_Absorber`, `Run_FinderColumns_ProjectRoot`, `Run_FocusTargetModel_SingleList`, `Run_GeometricSelectionEngine_NoAlloc`, `Run_DelegatePane`, `Run_FocusTargetModel_MapKeyRemoved`, `Run_CodeIssuesFinder_Readonly`, `Run_ResultsFormatter_Simplified`, `Run_NeoVisualTraceListener_SingleStamp`, `Run_FinderColumns_NoUnusedAlias`, `Run_NeoVisualLog_EnsurePane`, `Run_ResultMapper_ThreadSafety` |
| AC7 | The test infra is cleaned (M5/m49-m65/n21-n25): the timing tests are deterministic, the reflection is reduced, the assertions are strong, the helpers are complete | n/a | `Run_FzfFilter_GraceDelayToken_Deterministic`, `Run_FzfFilter_NoSpuriousTimeoutOnCancel_Deterministic`, `Run_FileContentCache_ThreadSafe_NoReflection`, `Run_TelescopeController_Dispose_Seam`, `Run_PaneFailureTracker_Interlocked_NoReflection`, `Run_GetCandidates_DefaultQuery_Pinned`, `Run_ResultsFormatter_RenderedTextLength_Consolidated`, `Run_TextMotionDispatcher_Motions_OnePerMethod`, `Run_ResultsColumns_MinMaxWidths_Messages`, `Run_FzfFilter_KillRegisteredBeforeWrite_Deterministic`, `Run_FileContentCache_IOOutsideLock_AssertEntered`, `Run_Assert_Equal_Message`, `Run_NeoVisual_NoReflection`, `Run_KeybindingConfig_Merge_MalformedJson`, `Run_KeybindingConfig_Merge_AssertEqual`, `Run_InjectedKeyGuard_Isolated`, `Run_FocusKeeper_TickCancelled_Deterministic`, `Run_Assert_Throws`, `Run_FzfFilter_TimeoutRace_Deterministic`, `Run_SetResults_Clamp`, `Run_TextInput_KeysIMapsToInsertStart_Fixed`, `Run_TextInput_StartsInInsertMode_Strong`, `Run_FocusKeeper_ResetAfterDispose_Real` |
| AC8 | The harness gates are hardened (m66-m71) | `editor-view-opened file=...` asserted order-independently (m66/m67); `leader-binding executed:` re-asserted on retry (m69) | e2e (deferred — E2E-CR107-7) + the harness code changes |
| AC9 | The docs + lint are refreshed (M6/m72-m76) | n/a | doc-ref + doc-content lints PASS |

## Unit test plan

- **`tests/Telescope.Tests`** (the hermetic seams): `Run_FzfFilter_FailureSignalsEmpty`
  (a timeout/crash returns `null`/throws — never the full list); `Run_FzfFinder_WhitespaceQueryEmpty`
  (a whitespace query shows 0 hits); `Run_FzfFinder_GatherOffThread` (the gather —
  read + BuildCandidates + FilterAsync + MapBatched + the literal scan — runs inside
  `Task.Run`); `Run_GrepFinder_SyncGetCandidatesThrows` (the sync `GetCandidates`
  throws for a non-empty query); `Run_CodeIssuesFinder_GatherAsync` (the TODO scan is
  async, the Error List walk stays UI-thread); `Run_GrepFinder_NoSpuriousCancelLog`
  (a cancelled gather logs nothing); `Run_QueryDrivenFinderBase_Shared` (Grep/Fzf
  inherit one base; the matcher hook is the only difference); `Run_FzfLineMapper_MapDeleted`;
  `Run_FileContentCache_NoToctou`; `Run_PreviewCaretMap_ReusesLineIndex`;
  `Run_ListKeyMap_SingleTable`; `Run_TextMotionDispatcher_HandleRemoved`;
  `Run_VisibilityByFinder_Scoped`; `Run_ShowOverlayAsync_AsyncGather`;
  `Run_ListPane_ShiftDistinction`; `Run_EnterInsert_CaretStyle`; `Run_PromptMotion_CtrlGuard`;
  `Run_OverlayCentering_Units`; `Run_ShowPreview_TryCatch`; `Run_HierarchyWalker_DeadRemoved`;
  `Run_FileFinder_SingleSeam`; `Run_RecentFileHit_Merged`; `Run_ColumnVisibilityModel_DeadRemoved`;
  `Run_ResultColumn_DeadRemoved`; `Run_FinderColumns_SharedBuilders`;
  `Run_KindAbbreviations_NoDefensive`; `Run_FilterFailureLog_Sanitized`;
  `Run_FilterFailureLog_Simplified`; `Run_FinderColumns_DataDriven`;
  `Run_ColumnWidths_Absorber`; `Run_FinderColumns_ProjectRoot`;
  `Run_FocusTargetModel_SingleList`; `Run_GeometricSelectionEngine_NoAlloc`;
  `Run_DelegatePane`; `Run_FocusTargetModel_MapKeyRemoved`; `Run_CodeIssuesFinder_Readonly`;
  `Run_ResultsFormatter_Simplified`; `Run_NeoVisualTraceListener_SingleStamp`;
  `Run_FinderColumns_NoUnusedAlias`; `Run_NeoVisualLog_EnsurePane`;
  `Run_ResultMapper_ThreadSafety`; plus the test-infra tests (AC7).
- **`tests/NeoVisual.Tests`** (the hermetic seams): `Run_WindowFrameAdapter_LinkedIsolation`
  (one stale frame is skipped, the rest survive); `Run_WindowRect_DeadMirrorsRemoved`;
  `Run_Direction_ExplicitMapping`; `Run_WindowNavigator_NullActive`;
  `Run_ExtractFrames_Removed`; `Run_VimBufferSubscriptions_NoDoubleDecrement`;
  `Run_VimModeSource_DetachCached`; `Run_InputHandler_SentinelArmedInProduction`;
  `Run_LeaderSequenceMatcher_SingleModifierVkSet`; `Run_InputHandler_SingleControllerResolution`;
  `Run_LeaderSequenceMatcher_ModifierChordPassesThrough`; `Run_KeyNames_ShiftAwareNonLetter`;
  `Run_PreviewEditorHost_CachePruned`; `Run_ErrorListGatherer_TtlInvalidated`;
  `Run_SolutionExplorer_FocusCacheInvalidated`; `Run_SolutionExplorer_SolutionFolderExpanded`;
  `Run_FocusKeeper_StopsOnEditorFocus`; `Run_WindowManager_AdviseSelectionHresult`;
  `Run_TextMotionHelper_SliceShared`; `Run_FocusKeeper_SetupShared`;
  `Run_BlockCaretAdornment_Frozen`; `Run_WindowManager_SingleResolution`;
  `Run_WindowManager_FocusChangeTryCatch`; `Run_StyleFocusedSurface_Delegates`;
  `Run_WindowManager_SingleWalk`; `Run_TextInput_KeysJKD0D4`;
  `Run_ToolWindowTypeResolver_ObjectSearchNotTextInput`; plus the test-infra tests (AC7).
- **RED proof:** every new test fails WITHOUT the fix and passes WITH it (the
  verify-tests-fail-without-fix discipline). The M3 RED is `Run_FzfFilter_FailureSignalsEmpty`
  (today the timeout/crash returns the full list). The M2 RED is
  `Run_WindowFrameAdapter_LinkedIsolation` (today one stale frame kills navigation).
  The M1 RED is `Run_InputHandler_SentinelArmedInProduction` (today the sentinel is
  never armed). The m1 RED is `Run_VimBufferSubscriptions_NoDoubleDecrement` (today the
  double-decrement drops the shared subscription). The m33 RED is the existing
  `Run_GrepFinder_*`/`Run_FzfFinder_*` suites (the base extraction must not change
  behavior).

## Diagnostics (M-M7)

- **No NEW harness-asserted diagnostic literals.** This plan is diagnostic-neutral
  except these diagnostic-BEHAVIOR changes (each flagged with its harness site) and two
  harness-INDEPENDENT failure-path literals (m17's `[NeoVisual] selection events advise
  failed: 0x{hr:X8}` — the C7 `window type probe failed` precedent, logged once; m32's
  `[Telescope] preview failed: {msg}` — the `OnSelected failed:`/`query gather failed:`
  family, defensive catch-and-log):
- **M3 (fzf failure):** a timeout/crash/whitespace query now shows 0 hits (or the
  literal fallback) instead of the full candidate list. Harness site: `telescope-fzf`
  (E2E-CR107-1 — a whitespace query is deterministic).
- **M1 (sentinel active):** `[NeoVisual] stale-toolwindow sentinel active` now fires
  when the harness creates the sentinel file — RESTORES the e2e gate. Harness site:
  `neovisual-explorer-move-editor-focus` (E2E-CR107-4).
- **m15 (select-file):** `solution-explorer select file=...` may now succeed where it
  logged `select none` (a project inside a collapsed solution folder). Harness site:
  `explorer-open-navigation` (E2E-CR107-5).
- **m16 (keeper timing):** the FocusKeeper stops when the user moves to the editor —
  the `solution-explorer select file=...` re-assert timing may change. Harness site:
  `explorer-open-navigation` (E2E-CR107-5).
- **m18 (text-motion caret):** `text-motion key=... caret=...` may change for w/e/$/j/k
  near the 4096-char slice end. Harness site: `neovisual-textinput-motions` +
  `explorer-searchbox-motions` (E2E-CR107-3).
- **m28 (List-pane shift):** `key=... mode=... handled=...` may change — `a` inserts
  after-caret, `A` at end, `i`/`I` at caret/start on the List pane. Harness site:
  `telescope-mode` (E2E-CR107-6).
- **m30 (Ctrl guard):** Ctrl+A/W/B/E/I/0 are no longer treated as plain vim keys.
  Harness site: `telescope-prompt-motions` + `telescope-preview-motions` (E2E-CR107-6).
- **m43 (sanitized filter-failed):** `[Telescope] filter failed: {msg}` is sanitized
  (no newline splitting). No harness dependency (a failure path).
- **m46 (boxText):** `results count={n} selected={m} boxText={len}` may change — the
  Files/Recent catalogs gain an absorber. Harness site: `telescope-results-columns`
  (E2E-CR107-6).
- **m9 (no spurious cancel log):** the `GrepFinder failed to enumerate:` line no longer
  fires on a cancelled gather. No harness dependency (a failure path).
- All other findings are diagnostic-neutral (the `[Telescope]`/`[NeoVisual]` literals
  byte-stable).

## Known-RED allowlist

- **NONE.** The baseline is all-GREEN (Telescope 319, NeoVisual 236, 44/44 e2e — the
  review verified both suites). Per-scenario flaky counts start at 0.

## E2E queue reference (e2e DEFERRED — queued in `e2e-queue.md`, status QUEUED)

> The user's 2026-10-06 instruction: "we are not on e2e capable machine so defer those to
> queue for later." The seven gates below are QUEUED at handoff (Step 7 — appended to
> `e2e-queue.md` on user approval, status QUEUED; never created/executed now); they become
> READY when this plan is GREEN and are created/executed on a capable machine.

- **E2E-CR107-1** — `telescope-grep` + `telescope-fzf` stay GREEN (M3/M4/m9/m33/m34/m38 —
  the fzf failure signaling + the off-thread gather + the QueryDrivenFinderBase; the
  `grep hits=`/`fzf hits=` contract; a whitespace query now shows 0 hits).
- **E2E-CR107-2** — `neovisual-window-nav` stays GREEN (M2/m10/m11/m12/m13 — the
  per-window try/catch + the dead-mirror deletion; `navigate direction=`/`navigate
  activated index=` unchanged).
- **E2E-CR107-3** — `neovisual-textinput-motions` + `explorer-searchbox-motions` +
  `neovisual-editor-insert` (m18 — the caret-relative slice fix; `text-motion key=...
  caret=...` may change near the slice end).
- **E2E-CR107-4** — `neovisual-explorer-move-editor-focus` (M1 — the sentinel now arms
  in production; `[NeoVisual] stale-toolwindow sentinel active` fires; the FocusGuard
  leak guard is actually exercised).
- **E2E-CR107-5** — `explorer-open-navigation` + `explorer-open-searchbox` (m15/m16 —
  the SolutionFolder Expanded + the FocusKeeper stop-on-editor-focus;
  `solution-explorer select file=...`).
- **E2E-CR107-6** — `telescope-preview` + `telescope-results-columns` + `telescope-mode`
  + `telescope-prompt-motions` + `telescope-preview-motions` (m23/m27/m28/m30/m31/m32/m46 —
  the preview LineIndex reuse + the async gather + the shift/Ctrl distinctions + the
  centering + the ShowPreview try/catch + the absorber; `results columns=`/`results
  count=`/`preview caret=`/`key=` contract).
- **E2E-CR107-7** — the full 44-scenario fresh-boot suite re-run (the regression gate;
  covers m66-m71 harness + everything).

## Telescope generalization analysis (user request)

The user asked whether the Telescope library can be made even more general/simplified.
A dedicated arch-auditor deep-dive (read every Telescope/ file) + web research
(Telescope.nvim architecture) concluded:

**Adopted (implemented in this plan):**
- **QueryDrivenFinderBase (Grep+Fzf)** — Phase 0 (m33): `GrepFinder.cs:44-161` ≈
  `FzfFinder.cs:51-192` — identical ctor, `EnumerateFiles`, `ContentCache`,
  `ToEntry`, `OpenHit`, empty-query short-circuit, `EnsureSolutionCache` (~100
  duplicated lines). The base owns all but a matcher hook. **The single biggest
  simplification win** — the next query-driven finder (e.g. a future symbols finder)
  inherits it. Risk: byte-exact `grep hits=`/`fzf hits=`/`opened grep:`/`opened fzf:`
  literals; pinned by `Run_GrepFinder_*`/`Run_FzfFinder_*`.
- **Shared column builders (FileColumn/LineColumn/TextColumn)** — Phase 5 (m41): the
  `"file"/"File"/28/6/30/Tail/true` repeats 4× (FinderColumns.cs:102,114,133,145),
  `"line"` 3×, `"text"` 2×. Single-sourced. Risk: `Run_ResultsColumns_*` pin exact values.
- **Dead production code deletion** — Phases 0/5 (m34, m35, m36, m39, m40, m42, m47,
  m48, n1-n20): the dead `FzfLineMapper.Map`, `HierarchyWalker.FirstFileEndingWith`,
  `ColumnVisibilityModel.VisibleColumns`, `ResultColumn.Width`/`WidthChars`, the
  `ImplMap` defensive entries, the dead `projectRoot` branch, the over-engineered
  `FilterFailureLog`/`ResultsFormatter` seams.

**Deferred (analyzed, not executed — documented for a future pass):**
- **Fold ReferencesFinder into SymbolFinderBase:** the 3 symbol-at-caret finders
  (References/Implementation/Definition) share ctor/gatherer/opener/OpenHit; only the
  display + gather-log differ. Deferred because the References finder's read/write
  access display + the `references gathered reads=... writes=...` diagnostic are pinned
  by ~10 tests + the e2e scenario; the churn is high for the win. Revisit when a 4th
  symbol finder is added.
- **ListFinderBase (File+Recent):** ~20 shared lines (ToEntry/OpenHit/`opened file:`
  literal); the gathers differ wholly. Marginal — revisit if a 3rd list finder appears.
- **A pluggable sorter registry / a third-party extension system / multi-select bulk
  actions:** the single fzf filter suffices; YAGNI for a personal VSIX (web research
  recommendation).

**Rejected (analyzed, not worth it for a VSIX):**
- **A single untyped Hit model:** the typed hit models (FileHit, RecentFileHit, GrepHit,
  DefinitionHit, ReferenceHit, CodeIssue) are the guard against payload-type confusion;
  FzfHit/ImplementationHit are already type aliases. The type-disjointness is pinned by
  tests.
- **The full Telescope.nvim picker/sorter/previewer/actions decomposition:** the repo
  already mirrors it (IFinder/FzfFilter/IPreviewEditor/IPane); more interfaces =
  over-engineering for 8 finders.
- **The overlay as a further-generic configurable picker host:** TelescopeOverlay is
  already generic picker plumbing; IPane/PaneHost is the lazygit foundation. Don't
  abstract fzf until a second sorter exists.
- **Replacing the overlay with native VS search** (Go To All / All-In-One Search /
  IVsWindowSearch): not extensible to custom finders, no vim motions, no custom
  columns, VS-themed.

## Files to be touched

- **Telescope/:** `Filter/FzfFilter.cs`, `Finders/FzfFinder.cs`, `Finders/GrepFinder.cs`,
  `Finders/CodeIssuesFinder.cs`, `Finders/FileFinder.cs`, `Finders/RecentFilesFinder.cs`,
  `Finders/Utils/FileContentCache.cs`, `Finders/Utils/FzfLineMapper.cs`,
  `Finders/Utils/HierarchyWalker.cs`, `Finders/Utils/RecentFileHit.cs`,
  `Finders/Utils/QueryDrivenFinderBase.cs` (NEW — m33), `Finders/FinderBase.cs`,
  `Overlay/TelescopeOverlay.cs`, `Overlay/Utils/PreviewCaretMap.cs`,
  `Overlay/Utils/Panes/ListPane.cs`, `Overlay/Utils/TextMotionDispatcher.cs`,
  `Overlay/Utils/FocusTargetModel.cs`, `Overlay/Utils/GeometricSelectionEngine.cs`,
  `Overlay/Utils/FinderColumns.cs`, `Overlay/Utils/ColumnWidths.cs`,
  `Overlay/Utils/ResultColumn.cs`, `Overlay/Utils/KindAbbreviations.cs`,
  `Overlay/Utils/ResultsFormatter.cs`, `Overlay/Utils/ResultMapper.cs`,
  `Overlay/Utils/Panes/PromptPane.cs`, `Overlay/Utils/Panes/PreviewPane.cs`,
  `Overlay/Utils/Panes/DelegatePane.cs` (NEW — n10), `Logging/Utils/FilterFailureLog.cs`,
  `Logging/Utils/NeoVisualTraceListener.cs`, `Logging/NeoVisualLog.cs`,
  `Logging/Utils/DiagnosticLog.cs`, `Logging/Utils/PaneFailureTracker.cs`.
- **MyExtension/:** `Input/InputHandler.cs`, `Input/Utils/LeaderSequenceMatcher.cs`,
  `Input/Utils/KeybindingConfig.cs`, `Input/Utils/KeyNames.cs`,
  `Input/Utils/StaleToolWindowSentinel.cs` (M1/BP-3),
  `Navigation/WindowNavigator.cs`, `Navigation/WindowNavigationEngine.cs`,
  `Navigation/Utils/WindowFrameAdapter.cs`, `Navigation/Utils/WindowFrameUtils.cs`,
  `Navigation/Utils/WindowRect.cs`, `Navigation/Utils/Direction.cs`,
  `Vim/Utils/VimBufferSubscriptions.cs`, `Vim/Utils/VimModeSource.cs`,
  `ToolWindows/WindowManager.cs`, `ToolWindows/SolutionExplorerController.cs`,
  `ToolWindows/TextInputToolWindowController.cs`, `ToolWindows/ToolWindowControllerBase.cs`
  (n23/BP-D21), `ToolWindows/Utils/TextMotionHelper.cs`,
  `ToolWindows/Utils/FocusKeeper.cs`, `ToolWindows/Utils/ToolWindowTypeResolver.cs`,
  `Adornments/BlockCaretAdornment.cs`, `Package/MyExtensionPackage.cs`,
  `Package/RoslynGatherers.cs`, `Package/Utils/PreviewEditorHost.cs`,
  `Package/Utils/PreviewTextCache.cs` (NEW — m7/BP-8),
  `Package/Utils/ErrorListGatherer.cs`, `Package/Utils/ErrorListCacheDecision.cs`
  (NEW — m8/BP-9).
- **tests/:** `tests/Telescope.Tests/Program.cs`, `tests/NeoVisual.Tests/Program.cs`,
  `tests/TestRunner.cs`.
- **tools/:** `tools/harness/test-e2e.ps1`, `tools/harness/iterate-telescope.ps1`.
- **docs/:** `docs/reviews/code-review.md`, `docs/spec.md`, `docs/progress.md`,
  `docs/e2e-queue.md`, `AGENTS.md`, `.opencode/skills/vs-extension-dev/SKILL.md`,
  `.opencode/agent/code-review-hub.md`, `.opencode/agent/neovim_review_hub.md`.

## Open risks

1. **M3's contract split is the highest-risk change.** The FileFinder path needs the
   full-list return; only the timeout/crash paths signal failure. The implementation-
   planner must pin the exact mechanism (return `null` vs a typed exception) against the
   real call sites (FzfFinder.cs:136 + TelescopeOverlay.cs:537).
2. **M4's sync `GetCandidates` throw breaks ~15 GrepFinder tests.** The test rewrites
   must land in the SAME step as the throw (Program.cs:3164-3344).
3. **m10's dead-mirror deletion breaks pinned tests.** `WindowRect.GapTo`/`Adjacency`
   (NeoVisual 2363/2371) + the PaneRect mirrors + `DirectionExtensions.ToChar` (live
   diagnostic) — the deletion is paired with the pinned-test updates.
4. **m1 needs a test BEFORE the fix.** The OnBufferClosed-then-Detach double-decrement
   is untested; the new signal must be pinned first.
5. **m16 must not break `explorer-open-navigation`.** The FocusKeeper stop-on-editor-
   focus change touches the same keeper `SelectFirstSourceFile` uses; the e2e gate
   (E2E-CR107-5) verifies.
6. **m46's absorber is a rendering change.** The Files/Recent catalogs gain an
   `int.MaxValue` absorber → the assigned widths change → `boxText=` may change; the
   `telescope-results-columns` gate verifies.
7. **m33's QueryDrivenFinderBase is a behavior-neutral refactor but touches the two
   most-tested finders.** The ~30 `Run_GrepFinder_*` + ~30 `Run_FzfFinder_*` tests pin
   the behavior; the base extraction must not change the `grep hits=`/`fzf hits=`/
   `opened grep:`/`opened fzf:` literals.
8. **The count discrepancy.** The summary's 79/22 split vs the table's 76/25 — the
   total is 107 either way; the Phase 8 doc refresh fixes the summary split.
9. **The queue position.** The 107-findings code-review-fixes plan becomes the FIRST
   pending item at handoff (before Gap 5) — the counts are re-reads at the gate.

---


---


---

# Build Plan (107 findings — unit-only; e2e DEFERRED to E2E-CR107-1..7, QUEUED)

> **Lane:** unit-only. RED is proven at the unit level (the new tests fail WITHOUT the fix, pass WITH it — verify-tests-fail-without-fix). Verify-with = unit test NAMES + diagnostic FORMATS only. The queued e2e gates (E2E-CR107-1..7) are noted as SECONDARY checks on the harness/doc steps; they are never the primary Verify-with.
> **Known-RED allowlist: NONE** (baseline all-GREEN: Telescope 319, NeoVisual 236, 44/44 e2e).
> **Build Plan authored by 4 parallel implementation-planners (2026-10-07), revised per the plan gate (2026-10-07) + the handoff doc-ref lint (BP-D36 added):** Section A (Phases 0-1, BP-1..BP-17), Section B (Phases 2-4, BP-1..BP-25), Section C (Phase 5, BP-1..BP-32), Section D (Phases 6-8, BP-D1..BP-D36). 110 BP steps covering all 107 findings.

---

# Section A — Build Plan

# Section A — Build Plan + Verification Trace (Phases 0-1)

> **Source:** `docs/reviews/code-review.md` (2026-10-07 refresh), rows M3/M4/m9/m33/m34/m38/n13/n14/n15
> (Phase 0) + M2/m10/m11/m12/m13/n4 (Phase 1). **Lane: unit-only** — Verify-with is unit test NAMES +
> diagnostic FORMATS only; e2e scenarios are DEFERRED to the queue (E2E-CR107-1/2). **Known-RED
> allowlist: NONE** (baseline all-GREEN: Telescope 319, NeoVisual 236).
>
> **Execution contract:** one top-to-bottom pass by the build-agent. Phase 0 ends with a mid-point
> verify (Telescope suite); Phase 1 ends with the final verify (NeoVisual + Telescope suites). The
> phase headers are hub checkpoints, not separate dispatches.
>
> **MEF/DI wiring:** NONE for Section A. The finders are constructed in `MyExtensionPackage.cs:104-107`
> (`_telescope.RegisterFinder(new GrepFinder(...))` / `new FzfFinder(...)` / `new CodeIssuesFinder(...)`);
> the m33 base extraction keeps the derived ctor signatures byte-identical, so no registration changes.
> No `[Export]`, no `default-keybindings.json`, no `InputHandler.ResolveAction` cases.

---

## Phase 0 — Query-driven finder core (M3, M4, m9, m33, m34, m38, n13, n14, n15)

### BP-1 — M3: FzfFilter signals failure distinctly (timeout/crash → `null`); FzfFinder treats it as literal-fallback; whitespace query → 0 hits; FileFinder path keeps the full-list degradation

- **Files:**
  - `Telescope/Filter/FzfFilter.cs` (modify)
  - `Telescope/Finders/FzfFinder.cs` (modify)
  - `Telescope/Overlay/TelescopeOverlay.cs` (modify — the FileFinder path at :537)
  - `tests/Telescope.Tests/Program.cs` (modify + add)
- **Change:**
  - `FzfFilter.FilterAsync`: the TIMEOUT path (`FzfFilter.cs:308`, after `TryKill` + the `fzf filter failed: timeout after {ms}ms` log + the fault-only continuation) and the CRASH path (`:329`, the generic `catch` after the `fzf filter failed: {msg}` log) now `return null` instead of `return lines`. **KEEP the full-list return** on: whitespace short-circuit (`:175-178`), cached-unavailable (`:183-186`), spawn-failure (`:226-229`), and cancellation (`:252`, `:283`) — those are the FileFinder-relevant graceful-degradation paths. The `fzf filter failed:` log lines are UNCHANGED (still emitted on timeout/crash).
  - `FzfFinder.GetCandidatesAsync`: the empty-query check at `:90` becomes `string.IsNullOrWhiteSpace(query)` (a whitespace query → 0 hits, no gather, no fzf spawn). When `_fzf.FilterAsync(...)` (`:136`) returns `null`, treat it as failure → fall back to the literal scan (the `else` branch's `LiteralLineScanner` loop) — NEVER map `null` back as matches. The `fzf hits={count}` log is unchanged.
  - `TelescopeOverlay.FilterAndUpdateAsync` (`:537`, the FileFinder path): coalesce the null failure signal back to the full snapshot so `MapBack` maps everything (the graceful-degradation contract): `var matched = await _fzf.FilterAsync(...) ?? snapshot.Select(x => x.Display).ToList();`.
- **Verify-with:**
  - NEW `Run_FzfFilter_FailureSignalsEmpty` (Telescope.Tests): a timeout (`hang.cmd` + `FilterTimeoutMs=200`) and a crash (`missing-fzf.exe`) both return `null` — never the full list.
  - NEW `Run_FzfFinder_WhitespaceQueryEmpty` (Telescope.Tests): `GetCandidatesAsync("   ")` → 0 entries AND `FakeFzfEngine.FilterCalls == 0` (no spawn).
  - NEW `Run_FzfFinder_FailureFallsBackToLiteral` (Telescope.Tests): a `FakeFzfEngine` whose `FilterAsync` returns `null` → the finder returns the literal-scan hits (never garbage).
  - UPDATE `Run_FzfFilter_TimeoutAwaitsTasks` (Telescope.Tests:1281): the timeout path now asserts `result == null` (was `result.Count == 1` + `Contains("alpha")`); keep the `AwaitedReadCount >= 2` assertion.
  - UPDATE `Run_FzfFilter_NonexistentPathFallsBackAndLogs` (Telescope.Tests:1253): the crash path now asserts `result == null` (was the full-list fallback); keep the `[Telescope] fzf filter failed:` log assertion.
  - STAY GREEN: `Run_FzfFilter_CancellationObservesTasks` (:1309 — cancellation still returns the full list), `Run_FzfFilter_FilterAsyncSkipsSpawnWhenUnavailable` (:1442), `Run_FzfFilter_TimeoutRace` (:1346 — the boundary-completed filter still returns its output), `Run_FzfFilter_NoSpuriousTimeoutOnCancel` (:5648), `Run_FzfFilter_GraceDelayToken` (:5683), `Run_FzfFinder_UnavailableFallsBackToLiteralScan` (:3697), `Run_FzfFinder_FuzzyMatchReportsHits` (:3542).
  - Diagnostic formats: `[Telescope] fzf filter failed: {msg}` and `[Telescope] fzf filter failed: timeout after {ms}ms` UNCHANGED (still emitted); `fzf hits={count}` UNCHANGED; a whitespace query now emits NO `fzf hits=` line (0 hits).
- **Fails-if:** `Run_FzfFilter_FailureSignalsEmpty` sees a non-null result (the timeout/crash path still returns `lines`); `Run_FzfFinder_WhitespaceQueryEmpty` sees a gather or a spawn (the `IsNullOrWhiteSpace` check is missing); `Run_FzfFinder_FailureFallsBackToLiteral` sees garbage hits (the null result was mapped back); `Run_FzfFilter_TimeoutAwaitsTasks`/`Run_FzfFilter_NonexistentPathFallsBackAndLogs` still assert the full-list return; the FileFinder path (`TelescopeOverlay.cs:537`) NREs on null (the coalesce is missing).

### BP-2 — M4a: FzfFinder moves the WHOLE gather (read + BuildCandidates + FilterAsync + MapBatched + literal scan) inside `Task.Run`

- **Files:** `Telescope/Finders/FzfFinder.cs` (modify); `tests/Telescope.Tests/Program.cs` (add).
- **Change:** In `FzfFinder.GetCandidatesAsync`, after the `IsNullOrWhiteSpace` short-circuit + `await _fzf.IsAvailableAsync()` + `EnumerateFiles()` (the DTE enumeration stays on the UI thread — it asserts `ThrowIfNotOnUIThread`), wrap the ENTIRE gather in ONE `Task.Run(async () => { ... }, cancellationToken)`: the per-file content reads, `FzfLineMapper.BuildCandidates`, `await _fzf.FilterAsync`, `FzfLineMapper.MapBatched`, AND the literal-fallback `LiteralLineScanner` loop. The `fzf hits={count}` log + `hits.Select(ToEntry)` stay outside (cheap). The caller's `cancellationToken` must still reach `FilterAsync` (the `Run_FzfFinder_Cancellation` contract).
- **Verify-with:**
  - NEW `Run_FzfFinder_GatherOffThread` (Telescope.Tests): a thread-recording `FakeFzfEngine` variant (records `Thread.CurrentThread.ManagedThreadId` inside `FilterAsync`) → the recorded thread != the test thread (the fzf path — BuildCandidates/FilterAsync/MapBatched — runs inside `Task.Run`). The literal path is already pinned off-thread by `Run_FzfFinder_LiteralFallbackOffThread` (:5567).
  - STAY GREEN: `Run_FzfFinder_BatchedFilter` (:5470 — `FilterCalls == 1`), `Run_FzfFinder_Cancellation` (:3736 — `LastToken == cts.Token`), `Run_FzfFinder_CacheEnumeratesOnce` (:3637), `Run_FzfFinder_HitCapBounded` (:3621), `Run_FzfFinder_QueryDrivenBehavior` (:3657).
  - Diagnostic: `fzf hits={count}` UNCHANGED.
- **Fails-if:** `Run_FzfFinder_GatherOffThread` records the test thread (the gather still runs on the post-`await` UI-thread continuation); `Run_FzfFinder_Cancellation` sees `LastToken != cts.Token` (the token was dropped in the `Task.Run`); `Run_FzfFinder_BatchedFilter` sees `FilterCalls != 1`.

### BP-3 — M4b: GrepFinder sync `GetCandidates` throws for a non-empty query; rewrite the non-empty GrepFinder tests in the SAME step

- **Files:** `Telescope/Finders/GrepFinder.cs` (modify); `tests/Telescope.Tests/Program.cs` (modify + add).
- **Change:** `GrepFinder.GetCandidates(string query)` (`:63-69`) stops delegating to `GetCandidatesAsync(...).GetAwaiter().GetResult()` (the UI-thread block). New contract (mirrors `FzfFinder.GetCandidates`): `string.IsNullOrEmpty(query)` → `Array.Empty<FinderEntry>()`; a NON-empty query → `throw new NotSupportedException("GrepFinder is query-driven; call GetCandidatesAsync(query)")`. The async `GetCandidatesAsync` is unchanged (the production path).
- **Verify-with:**
  - NEW `Run_GrepFinder_SyncGetCandidatesThrows` (Telescope.Tests): `GetCandidates("")` → 0 entries (no throw); `GetCandidates("needle")` → throws `NotSupportedException`.
  - REWRITE the 8 GrepFinder tests that call the sync `GetCandidates("non-empty")` to use `GetCandidatesAsync(...).GetAwaiter().GetResult()` (or `async Task` + `await`): `Run_GrepFinder_LineScanMatchesCaseInsensitive` (:3152), `Run_GrepFinder_DisplayIsFileNameLineText` (:3171), `Run_GrepFinder_PayloadRoundTripsGrepHit` (:3185), `Run_GrepFinder_OnSelectedOpensHitAtLine` (:3204), `Run_GrepFinder_HitCapBounded` (:3223), `Run_GrepFinder_CacheEnumeratesOnce` (:3247), `Run_GrepFinder_OffThreadScanSameHits` (:3267), `Run_GrepFinder_QueryDrivenBehavior` (:3328). The empty-query tests (`Run_GrepFinder_EmptyQueryReturnsZeroCandidates` :3140, `Run_GrepFinder_EmptyQueryCleanEmptyNoFailureLog` :3300) stay as-is (they call `GetCandidates("")`).
  - STAY GREEN: `Run_GrepFinder_GetCandidatesAsync_NoBlock` (:5441), `Run_GrepFinder_ScanFileBackground` (:5357), `Run_GrepFinder_SharedInvalidation` (:3356).
  - Diagnostic: `grep hits={count}` UNCHANGED.
- **Fails-if:** `Run_GrepFinder_SyncGetCandidatesThrows` sees `GetCandidates("needle")` return hits (the throw is missing); any rewritten test still calls the sync `GetCandidates("non-empty")` and throws (the rewrite was missed); `Run_GrepFinder_EmptyQueryReturnsZeroCandidates` throws on `GetCandidates("")` (the empty short-circuit was dropped).

### BP-4 — M4c: CodeIssuesFinder gather becomes async (TODO scan off-thread, COM Error List walk stays UI-thread)

- **Files:** `Telescope/Finders/CodeIssuesFinder.cs` (modify); `tests/Telescope.Tests/Program.cs` (add).
- **Change:** `CodeIssuesFinder` overrides `GetCandidatesAsync` (the async entry) to run the gather asynchronously: the TODO scan runs via `await Task.Run(...)` (off-thread, NON-blocking — no `.GetAwaiter().GetResult()` on the UI thread), then the COM Error List walk (`CollectErrorList`, which asserts `ThrowIfNotOnUIThread`) runs on the UI thread after the await resumes. The sync `GatherHits()` (still called by the base `GetCandidates()` at overlay-open `TelescopeOverlay.cs:388`) delegates to the async path so the existing sync callers/tests keep working. The `opened issue: {path} line={line}` + `Error List read failed: {msg}` literals are unchanged.
- **Verify-with:**
  - NEW `Run_CodeIssuesFinder_GatherAsync` (Telescope.Tests): a gate-blocked `reader` delegate; `GetCandidatesAsync("")` returns a PENDING task while the TODO scan is blocked (the calling thread is free — no `.GetAwaiter().GetResult()`); after the gate is set, the task completes with the hits. The COM Error List walk stays on the UI thread (the hermetic seam has no Error List, so this is pinned by the code shape + the existing `Run_CodeIssuesFinder_TodoScanOffThread` :5595 which stays GREEN).
  - STAY GREEN: `Run_CodeIssuesFinder_TodoScanOffThread` (:5595), `Run_FileContentCache_Shared` (:5420), `Run_FileContentCache_RequiredParam` (:3911).
  - Diagnostic: `opened issue: {path} line={line}` UNCHANGED.
- **Fails-if:** `Run_CodeIssuesFinder_GatherAsync` sees the task complete while the reader is blocked (the gather still blocks the calling thread); `Run_CodeIssuesFinder_TodoScanOffThread` records the test thread (the TODO scan moved back onto the UI thread).

### BP-5 — m9: suppress the spurious `GrepFinder failed to enumerate:` line on a cancelled gather

- **Files:** `Telescope/Finders/GrepFinder.cs` (modify); `tests/Telescope.Tests/Program.cs` (add).
- **Change:** In `GrepFinder.GetCandidatesAsync`, the `catch (Exception ex)` around the `await Task.Run(...)` scan (`:128-131`) must NOT log `GrepFinder failed to enumerate: {msg}` when the gather was cancelled. Add a `catch (OperationCanceledException)` BEFORE the generic catch that returns silently (the overlay's `RefreshQueryDrivenAsync` already treats `OperationCanceledException` as a silent return at `TelescopeOverlay.cs:525`).
- **Verify-with:**
  - NEW `Run_GrepFinder_NoSpuriousCancelLog` (Telescope.Tests): a GrepFinder WITHOUT the hermetic enumerate seam (the DTE path), `GetCandidatesAsync("NEEDLE", preCancelledToken)` → 0 entries AND the log contains NO `[Telescope] GrepFinder failed to enumerate:` line.
  - STAY GREEN: `Run_GrepFinder_EmptyQueryCleanEmptyNoFailureLog` (:3300), `Run_GrepFinder_GetCandidatesAsync_NoBlock` (:5441).
  - Diagnostic: the `[Telescope] GrepFinder failed to enumerate: {msg}` line no longer fires on cancel (a failure path — no harness dependency).
- **Fails-if:** `Run_GrepFinder_NoSpuriousCancelLog` sees the `GrepFinder failed to enumerate:` line (the `OperationCanceledException` still hits the generic catch).

### BP-6 — m33: extract the shared `QueryDrivenFinderBase<THit>` (GrepFinder + FzfFinder inherit; matcher hook is the only difference)

- **Files:**
  - `Telescope/Finders/Utils/QueryDrivenFinderBase.cs` (NEW)
  - `Telescope/Finders/GrepFinder.cs` (modify — inherit the base)
  - `Telescope/Finders/FzfFinder.cs` (modify — inherit the base)
  - `tests/Telescope.Tests/Program.cs` (add)
- **Change:** New `internal abstract class QueryDrivenFinderBase<THit> : FinderBase<THit> where THit : IFileLocation` owning the ~100 duplicated lines from `GrepFinder.cs:44-161` ≈ `FzfFinder.cs:51-192`: the ctor `(Func<DTE> dteFactory, ProjectFileCache fileCache, FileContentCache contentCache, Func<IReadOnlyList<string>>? testEnumerate = null, Action<THit>? testOpener = null)`, the `ContentCache` seam, `EnumerateFiles()` (with a virtual enumerate-failure literal), `ToEntry` (the `{fileName}:{line}: {lineText}` display), `OpenHit` (with a virtual open literal), the whitespace-query short-circuit — **PINNED to `string.IsNullOrWhiteSpace(query)`** (the base's single predicate; consistent with BP-1's FzfFinder `IsNullOrWhiteSpace` change, which the base absorbs so it is NOT lost), `EnsureSolutionCache`, and the async gather skeleton (BP-2's `Task.Run` shape). The derived classes provide ONLY: `Name`, the async matcher hook (`protected abstract Task<IReadOnlyList<THit>> MatchAsync(IReadOnlyList<string> files, string query, CancellationToken token)`), and the log literals (`grep hits=`/`fzf hits=`/`opened grep:`/`opened fzf:`/`GrepFinder failed to enumerate:`/`FzfFinder failed to enumerate:`). `GrepFinder`'s ctor keeps its 5-arg signature; `FzfFinder`'s ctor keeps its 6-arg signature (adds the `IFzfEngine`). **Behavior-neutral EXCEPT one intentional, flagged change:** the base's `IsNullOrWhiteSpace` short-circuit changes GrepFinder's whitespace-query behavior — a whitespace query now returns 0 entries with NO `grep hits=` line (previously `IsNullOrEmpty` at GrepFinder.cs:76 let a whitespace query through to the scan). This mirrors the behavior BP-1 already pins for FzfFinder (`Run_FzfFinder_WhitespaceQueryEmpty`) and is covered by the deferred `telescope-grep` gate E2E-CR107-1. The byte-exact literals must survive.
- **Verify-with:**
  - NEW `Run_QueryDrivenFinderBase_Shared` (Telescope.Tests): `typeof(GrepFinder).BaseType == typeof(QueryDrivenFinderBase<GrepHit>)` and `typeof(FzfFinder).BaseType == typeof(QueryDrivenFinderBase<FzfHit>)`; the shared `ContentCache` seam + the `{fileName}:{line}: {lineText}` display behave identically through both finders.
  - **DIAGNOSTIC-BEHAVIOR NOTE (the flagged Grep whitespace-query change):** the base's `IsNullOrWhiteSpace` short-circuit means `GrepFinder.GetCandidatesAsync("   ")` now returns 0 entries with NO `grep hits=` line (previously `IsNullOrEmpty` at GrepFinder.cs:76 let a whitespace query through to the scan). This is the SAME behavior BP-1 pins for FzfFinder (`Run_FzfFinder_WhitespaceQueryEmpty`) and is covered by the deferred `telescope-grep` gate E2E-CR107-1 — it must NOT be reported as a regression. No existing `Run_GrepFinder_*` test asserts a whitespace-query `grep hits=` line, so the ~30 finder tests stay GREEN.
  - STAY GREEN (the behavior pins): the ~30 `Run_GrepFinder_*` + ~30 `Run_FzfFinder_*` tests, incl. `Run_GrepFinder_DisplayIsFileNameLineText` (:3171), `Run_FzfFinder_DisplayIsFileNameLineText` (:3569), `Run_GrepFinder_OnSelectedOpensHitAtLine` (:3204), `Run_FzfFinder_OnSelectedOpensHitAtLine` (:3602), `Run_FileContentCache_Shared` (:5420), `Run_FileContentCache_RequiredParam` (:3911).
  - Diagnostic formats (byte-exact, the m33 risk): `grep hits={count}`, `fzf hits={count}`, `opened grep: file={path} line={line}`, `opened fzf: file={path} line={line}`.
- **Fails-if:** any `Run_GrepFinder_*`/`Run_FzfFinder_*` test fails (the base extraction changed behavior); a literal drifts (`grep hits=`/`fzf hits=`/`opened grep:`/`opened fzf:`); `Run_QueryDrivenFinderBase_Shared` sees a non-base `BaseType` (the inheritance is wrong); a derived ctor signature changed (the `MyExtensionPackage.cs:104-107` construction sites or the test ctors break); a whitespace query still emits a `grep hits=` line (the base's `IsNullOrWhiteSpace` short-circuit was dropped or reverted to `IsNullOrEmpty` — the flagged Grep behavior change did not land).

### BP-7 — m34: delete the dead `FzfLineMapper.Map` (test-only; `MapBatched` supersedes)

- **Files:** `Telescope/Finders/Utils/FzfLineMapper.cs` (modify); `tests/Telescope.Tests/Program.cs` (modify + add).
- **Change:** Delete `FzfLineMapper.Map(string[] fileLines, IReadOnlyList<string> matchedLines)` (`FzfLineMapper.cs:22-51`) — no production callers (FzfFinder uses `BuildCandidates` + `MapBatched` only). Keep `BuildCandidates` + `MapBatched`.
- **Verify-with:**
  - NEW `Run_FzfLineMapper_MapDeleted` (Telescope.Tests): reflection-absence — `typeof(FzfLineMapper).GetMethod("Map") == null`.
  - DELETE the 3 `Map`-pinning tests: `Run_FzfLineMapper_ExactLineMapsToLineNumber` (:3441), `Run_FzfLineMapper_DuplicateLinesMapOrdinal` (:3452), `Run_FzfLineMapper_UnknownLineSkipped` (:3466).
  - STAY GREEN: `Run_FzfLineMapper_Boundary` (:5495 — `BuildCandidates`/`MapBatched`), `Run_FzfFinder_BatchedFilter` (:5470).
  - Diagnostic: none (dead code).
- **Fails-if:** `Run_FzfLineMapper_MapDeleted` finds `Map` (the method survived); a deleted test still compiles (the deletion was missed); `Run_FzfLineMapper_Boundary` fails (a `MapBatched` regression).

### BP-8 — m38: fix the `FileContentCache` TOCTOU in the double-checked `GetLines`

- **Files:** `Telescope/Finders/Utils/FileContentCache.cs` (modify); `tests/Telescope.Tests/Program.cs` (add).
- **Change:** In `GetLines` (`FileContentCache.cs:51-85`), the file can be modified between the outside-lock `_reader(path)` read and the second-lock re-check, so stale lines get cached under a fresh timestamp (`:72-84`). Fix: capture the timestamp BEFORE the outside-lock read; after the read, re-read the timestamp; if it changed during the read, re-read the content (bounded retry) or skip caching — NEVER cache stale lines under a fresh timestamp. The IO-outside-lock contract (`Run_FileContentCache_IOOutsideLock` :5530) and the double-checked-hit contract (`Run_FileContentCache_CachedRead` :3766) must be preserved. Apply the same guard to `GetContent` (`:96-124`) if it shares the pattern.
- **Verify-with:**
  - NEW `Run_FileContentCache_NoToctou` (Telescope.Tests): a `timestamp` delegate returning `t0` on the first call and `t1` on every later call (simulating a file modified during the outside-lock read) + a counting `reader`; the first `GetLines("a")` must NOT cache the stale lines under `t1` — a second `GetLines("a")` re-reads (`reader` called twice). RED today: the stale lines are cached under `t1` and the second read is a hit (`reader` called once).
  - STAY GREEN: `Run_FileContentCache_IOOutsideLock` (:5530), `Run_FileContentCache_CachedRead` (:3766), `Run_FileContentCache_InvalidatesOnTimestampChange` (:3784), `Run_FileContentCache_ThreadSafe` (:3846), `Run_FileContentCache_Lru` (:3932), `Run_FileContentCache_SharedEntry` (:5623).
  - Diagnostic: none (a cache-internal fix).
- **Fails-if:** `Run_FileContentCache_NoToctou` sees the reader called once (the stale lines were cached under the fresh timestamp); `Run_FileContentCache_IOOutsideLock` fails (the fix re-introduced IO inside the lock); `Run_FileContentCache_ThreadSafe` throws (the fix broke the concurrency contract).

### BP-9 — n13: drop the redundant `lines.ToArray()` in FzfFinder (LiteralLineScanner.Scan takes `IReadOnlyList<string>`)

- **Files:** `Telescope/Finders/Utils/LiteralLineScanner.cs` (modify); `Telescope/Finders/FzfFinder.cs` (modify).
- **Change:** `LiteralLineScanner.Scan` signature `string[] lines` → `IReadOnlyList<string> lines` (`LiteralLineScanner.cs:14`; `string[]` implements it, so `GrepFinder.ScanFile`'s `cache.GetLines(path)` call is unaffected). Drop the redundant `.ToArray()` at `FzfFinder.cs:151` (`LiteralLineScanner.Scan(lines.ToArray(), ...)` → `Scan(lines, ...)`).
- **Verify-with:** STAY GREEN: `Run_LiteralLineScanner_Direct` (:5713 — passes `string[]` literals, which satisfy `IReadOnlyList<string>`), `Run_GrepFinder_ScanFileBackground` (:5357), `Run_FzfFinder_LiteralFallbackOffThread` (:5567). The build compiles (no `.ToArray()` call-site drift).
- **Fails-if:** a compile error at a `Scan` call site (the signature change was not propagated); `Run_LiteralLineScanner_Direct` fails (the `IReadOnlyList<string>` overload changed the null/empty/cap guards).

### BP-10 — n14: remove the `FileFinder._fileCache!.Get` null-forgiving

- **Files:** `Telescope/Finders/FileFinder.cs` (modify).
- **Change:** At `FileFinder.cs:68` (`_fileCache!.Get(_testEnumerate)` in the `_testEnumerate != null` branch), remove the `!` null-forgiving by handling the legitimately-null cache: `_fileCache?.Get(_testEnumerate) ?? Array.Empty<string>()` (a test enumerate with no cache degrades to empty instead of a latent NPE). The `_fileCache != null` DTE path (`:87-89`) is unchanged.
- **Verify-with:** STAY GREEN: `Run_FileFinder_UsesProjectFileCache` (:2529), `Run_FileFinder_EnumeratesCandidates`/`Run_FileFinder_OpenMissingFileIsNoOp` (:2506), `Run_GetCandidates_IsQueryDriven` (:4050). The build compiles with nullable analysis clean (no `!` on `_fileCache`).
- **Fails-if:** a compile warning/error on the `_fileCache` dereference (the null-forgiving was removed without a null-handling path); `Run_FileFinder_UsesProjectFileCache` fails (the `?.` broke the cache-served enumerate).

### BP-11 — n15: delete the never-emitted `DiagnosticLog.GlobalKeyboard` constant

- **Files:** `Telescope/Logging/Utils/DiagnosticLog.cs` (modify); `tests/Telescope.Tests/Program.cs` (modify).
- **Change:** Delete `public const string GlobalKeyboard = "[GlobalKeyboard] ";` (`DiagnosticLog.cs:15`) — no production log site emits it (grep-verified: only the test pins it). **PAIRED:** remove the `Assert.Equal("[GlobalKeyboard] ", DiagnosticLog.GlobalKeyboard);` line from `Run_LogPrefixes_Pinned` (`tests/Telescope.Tests/Program.cs:4076`) in the SAME step.
- **Verify-with:** STAY GREEN: `Run_LogPrefixes_Pinned` (:4070 — now asserts the 4 remaining prefixes `[NeoVisual]`/`[Telescope]`/`[Hook]`/`[MyExtension]`). The build compiles (no `DiagnosticLog.GlobalKeyboard` reference survives). Doc-ref lint (`pwsh tools/lint/check-doc-refs.ps1`) PASSES — the constant is not backticked in any linted doc (AGENTS.md/SKILL.md/spec/progress/architecture-review/code-review/e2e-queue/.opencode/agent/*.md; `docs/implementation_plan.md` is lint-excluded by design).
- **Fails-if:** `Run_LogPrefixes_Pinned` still references `DiagnosticLog.GlobalKeyboard` (the paired test update was missed → compile error); a production log site references the deleted constant (the grep was wrong).

**Phase 0 mid-point verify (hub checkpoint):** `dotnet run --project tests/Telescope.Tests` — the 9 new Phase-0 tests (`Run_FzfFilter_FailureSignalsEmpty`, `Run_FzfFinder_WhitespaceQueryEmpty`, `Run_FzfFinder_FailureFallsBackToLiteral`, `Run_FzfFinder_GatherOffThread`, `Run_GrepFinder_SyncGetCandidatesThrows`, `Run_CodeIssuesFinder_GatherAsync`, `Run_GrepFinder_NoSpuriousCancelLog`, `Run_QueryDrivenFinderBase_Shared`, `Run_FzfLineMapper_MapDeleted`, `Run_FileContentCache_NoToctou`) pass and the existing Telescope suite (319) stays GREEN. `grep hits=`/`fzf hits=`/`opened grep:`/`opened fzf:` byte-stable.

---

## Phase 1 — Navigation isolation (M2, m10, m11, m12, m13, n4)

### BP-12 — M2: per-window try/catch around `CompareWindows` in `FindActive`'s `FirstOrDefault` predicate + `LinkedTo`'s `Where` clause

- **Files:** `MyExtension/Navigation/Utils/WindowFrameAdapter.cs` (modify); `tests/NeoVisual.Tests/Program.cs` (add).
- **Change:** `FindActive` (`WindowFrameAdapter.cs:105`) and `LinkedTo` (`:121`) re-read `DteWindow.Caption`/`Type` via `WindowFrameUtils.CompareWindows` with NO per-window try/catch — one stale RCW still kills all navigation (the F7 residual). Wrap each `CompareWindows` call in the same per-window try/catch `GetLinkedWindowsList` uses (`WindowFrameUtils.cs:63-75`): a throwing window is SKIPPED, the rest survive. `FindActive`'s predicate becomes a helper that returns false on a throwing window; `LinkedTo`'s `Where` clause skips throwing adapters.
- **Verify-with:**
  - NEW `Run_WindowFrameAdapter_LinkedIsolation` (NeoVisual.Tests): fake adapters (via `GetUninitializedObject` + a `_dte` reflection set, mirroring `Run_WindowNavigator_CacheClearedOnReenum` :4023) where ONE adapter's `DteWindow` throws on `Caption`/`Type` (a `ThrowingWindow` fake, mirroring `ThrowingLinkedWindow` :4930); `LinkedTo`/`FindActive` skip the stale adapter and return the surviving ones — no exception escapes. `TestScaffold.SetCurrentDispatcherAsUiThread()` wraps the call.
  - STAY GREEN: `Run_WindowFrameUtils_LinkedWindowIsolation` (:4313 — the `GetLinkedWindowsList` isolation), `Run_WindowNavigator_BuildActiveWindowsNullActive` (:2630), `Run_WindowNavigator_CacheClearedOnReenum` (:4023), `Run_WindowNavigator_LazyDte` (:4091).
  - Diagnostic: `[NeoVisual] navigate direction={direction.ToChar()}` / `[NeoVisual] navigate activated index={n}` / `[NeoVisual] navigate no-op: {reason}` UNCHANGED.
- **Fails-if:** `Run_WindowFrameAdapter_LinkedIsolation` sees an exception escape (the per-window try/catch is missing in one of the two sites); a surviving adapter is dropped (the skip logic is too broad).

### BP-13 — m10: delete the dead `WindowRect`/`PaneRect` `GapTo`/`Adjacency` mirrors + `DirectionExtensions.PerpendicularAxis`/`Axis`/`PaneAxis` (PAIRED with the pinned-test updates)

- **Files:**
  - `MyExtension/Navigation/Utils/WindowRect.cs` (modify — delete `Adjacency` :35-49 + `GapTo` :51-61)
  - `MyExtension/Navigation/Utils/Direction.cs` (modify — delete `PerpendicularAxis` :10 + the `Axis` enum :5)
  - `Telescope/Overlay/Utils/FocusTargetModel.cs` (modify — delete `PaneRect.Adjacency` :88-99 + `PaneRect.GapTo` :104-114 + the `PaneAxis` enum :119)
  - `tests/NeoVisual.Tests/Program.cs` (modify + add)
- **Change:** Delete the dead mirrors of the shared `GeometricSelectionEngine` formulas (the engine at `GeometricSelectionEngine.cs:129-154` is the single source). `WindowRect` keeps `X/Y/Width/Height/Right/Bottom/IsEmpty` + the `IGeometricRect` explicit properties; `PaneRect` keeps its auto-properties + `IGeometricRect`. **KEEP `DirectionExtensions.ToChar`** (live — pinned by `Run_Direction_ToChar` :2553 + the `navigate direction=` diagnostic). **PAIRED:** delete the pinned tests `Run_RectCoordinate_Adjacency` (NeoVisual.Tests:2363) + `Run_RectCoordinate_GapTo` (:2371) in the SAME step.
- **Verify-with:**
  - NEW `Run_WindowRect_DeadMirrorsRemoved` (NeoVisual.Tests): reflection-absence — `typeof(WindowRect).GetMethod("Adjacency") == null` && `GetMethod("GapTo") == null` (the only hermetic way to pin a deletion; a single reflection call, not systemic).
  - STAY GREEN (behavior preserved through the shared engine): `Run_WindowNavigationEngine_*` (:2411-2544), `Run_WindowNavigationEngine_EmptyRectExcludedBySelectTarget` (:2608), `Run_Direction_ToChar` (:2553), and the Telescope.Tests `Run_FocusTarget_*` + `Run_SharedGeometricEngine_Parameters` (:5739) — none call `PaneRect.Adjacency`/`GapTo` directly (verified: they construct `PaneRect` values and delegate to `GeometricSelectionEngine.SelectTarget`).
  - Diagnostic: `[Telescope] focus target=Input|List|Preview` UNCHANGED (the FocusTargetModel tie-break is pinned by the shared engine); `[NeoVisual] navigate direction=...` UNCHANGED.
- **Fails-if:** `Run_WindowRect_DeadMirrorsRemoved` finds `Adjacency`/`GapTo` (a mirror survived); a `Run_WindowNavigationEngine_*` test fails (the shared engine no longer reproduces the window contract); a `Run_FocusTarget_*` test fails (the PaneRect deletion broke the pane focus); `Run_Direction_ToChar` fails (ToChar was wrongly deleted).

### BP-14 — m11: drop the unused `caption` parameter of `WindowFrameUtils.MatchesPropertiesQuirk`

- **Files:** `MyExtension/Navigation/Utils/WindowFrameUtils.cs` (modify).
- **Change:** `MatchesPropertiesQuirk(string caption, vsWindowType type, vsWindowType otherType)` (`:120`) → `MatchesPropertiesQuirk(vsWindowType type, vsWindowType otherType)` (the `caption` param is never read). Update the single call site at `:105` (`MatchesPropertiesQuirk(lhs.Caption, lhs.Type, rhs.Type)` → `MatchesPropertiesQuirk(lhs.Type, rhs.Type)`).
- **Verify-with:** the build compiles (the call site updated); STAY GREEN: `Run_WindowFrameUtils_LinkedWindowIsolation` (:4313), `Run_WindowFrameAdapter_LinkedIsolation` (BP-12). No dedicated test (the method is a private-ish helper with no direct test).
- **Fails-if:** a compile error at `:105` (the call site was not updated); a stale 3-arg call remains.

### BP-15 — m12: replace the `(int)direction` coupling in `WindowNavigationEngine` with an explicit mapping

- **Files:** `MyExtension/Navigation/WindowNavigationEngine.cs` (modify); `tests/NeoVisual.Tests/Program.cs` (add).
- **Change:** `WindowNavigationEngine.SelectTarget` (`:16`) passes `(int)direction` to `GeometricSelectionEngine.SelectTarget` — silently coupling the `Direction` enum declaration order to the engine's int tokens (0=Up, 1=Down, 2=Left, 3=Right). Replace with an explicit switch mapping `Direction` → `GeometricSelectionEngine.Up/Down/Left/Right` (mirroring `FocusTargetModel.DirectionOf` at `FocusTargetModel.cs:310-320`), so a future `Direction` reorder cannot remap Up↔Down↔Left↔Right silently.
- **Verify-with:**
  - NEW `Run_Direction_ExplicitMapping` (NeoVisual.Tests): for each of the 4 directions, `WindowNavigationEngine.SelectTarget(active, candidates, d, settings)` produces the same target as `GeometricSelectionEngine.SelectTarget(active, candidates, <explicit token>, allowNegativeGap: true, divide: <settings divide>, strictEdge: true)` — pinning the Direction→token correspondence independent of the enum's numeric values.
  - STAY GREEN: `Run_WindowNavigationEngine_*` (:2411-2544), `Run_Direction_ToChar` (:2553).
  - Diagnostic: `[NeoVisual] navigate direction={direction.ToChar()}` UNCHANGED.
- **Fails-if:** `Run_Direction_ExplicitMapping` sees a direction map to the wrong token (the switch is wrong); a `Run_WindowNavigationEngine_*` test fails (the mapping changed the selection).

### BP-16 — m13: handle the legitimately-null `_activeWindow` path in `WindowNavigator`

- **Files:** `MyExtension/Navigation/WindowNavigator.cs` (modify); `tests/NeoVisual.Tests/Program.cs` (add).
- **Change:** `_activeWindow = WindowFrameAdapter.FindActive(activeWindow, _activeWindows) ?? null!;` (`:88`) hides the legitimately-null path from the compiler. Declare the field `private WindowFrameAdapter? _activeWindow;` (`:16`) and drop the `?? null!` (assign the `FindActive` result directly). The existing runtime guard at `:137` (`if (_activeWindow == null || _activeWindows.Count == 0)`) already returns `NavigationOutcome.NoOp("no active window")` — the nullable field makes the compiler enforce it.
- **Verify-with:**
  - NEW `Run_WindowNavigator_NullActive` (NeoVisual.Tests): a `WindowNavigator` whose active window cannot be paired to an adapter (a `FakeWindow` active + adapters whose `DteWindow` is null, so `FindActive` returns null) → `NavigateInDirection` returns `NavigationOutcome.NoOp("no active window")` without throwing. `TestScaffold.SetCurrentDispatcherAsUiThread()` + the `RecordingPackage`/`FakeWindow` scaffolding (mirroring `Run_WindowNavigator_LazyDte` :4091).
  - STAY GREEN: `Run_WindowNavigator_BuildActiveWindowsNullActive` (:2630), `Run_WindowNavigator_CacheClearedOnReenum` (:4023), `Run_WindowNavigator_LazyDte` (:4091).
  - Diagnostic: `[NeoVisual] navigate no-op: no active window` UNCHANGED (the m47 outcome diagnostic).
- **Fails-if:** `Run_WindowNavigator_NullActive` sees a throw (the null path is not handled); a compile warning on the nullable dereference (the field is still non-nullable).

### BP-17 — n4: delete the redundant `WindowFrameAdapter.ExtractFrames` wrapper

- **Files:** `MyExtension/Navigation/Utils/WindowFrameAdapter.cs` (modify); `tests/NeoVisual.Tests/Program.cs` (add).
- **Change:** Delete the private `ExtractFrames(IEnumWindowFrames)` wrapper (`:124-128`) that only forwards to `ExtractFramesCore`. Update the two call sites at `:74` and `:85` to call `ExtractFramesCore(...)` directly.
- **Verify-with:**
  - NEW `Run_ExtractFrames_Removed` (NeoVisual.Tests): reflection-absence — `typeof(WindowFrameAdapter).GetMethod("ExtractFrames", NonPublic|Static) == null`.
  - STAY GREEN: `Run_WindowFrameAdapter_LinkedIsolation` (BP-12), `Run_WindowAdapter_TryGetScreenRect_NullFrameReturnsNull` (:2598).
  - Diagnostic: `[NeoVisual] window frame enumeration failed: {msg}` / `[NeoVisual] window frame skipped: {msg}` UNCHANGED (the per-enumeration fault isolation is preserved).
- **Fails-if:** `Run_ExtractFrames_Removed` finds `ExtractFrames` (the wrapper survived); a compile error at `:74`/`:85` (the call sites were not updated).

**Phase 1 final verify (hub checkpoint):** `dotnet run --project tests/NeoVisual.Tests` (the 5 new Phase-1 tests + the existing 236 stay GREEN) AND `dotnet run --project tests/Telescope.Tests` (the m10 `PaneRect` deletion must not break the `Run_FocusTarget_*`/`Run_SharedGeometricEngine_Parameters` tests). `navigate direction=`/`navigate activated index=`/`focus target=` byte-stable.

---

## Verification Trace

| failing test / scenario | implicated steps | expected diagnostic |
|---|---|---|
| `Run_FzfFilter_FailureSignalsEmpty` (NEW) | BP-1 | `[Telescope] fzf filter failed: {msg}` / `fzf filter failed: timeout after {ms}ms` still logged; the RETURN is `null` (never the full list) |
| `Run_FzfFinder_WhitespaceQueryEmpty` (NEW) | BP-1 | no `fzf hits=` line (a whitespace query → 0 hits, no spawn) |
| `Run_FzfFinder_FailureFallsBackToLiteral` (NEW) | BP-1 | `fzf hits={count}` from the literal scan (never garbage) |
| `Run_FzfFilter_TimeoutAwaitsTasks` (UPDATED) | BP-1 | `fzf filter failed: timeout after {ms}ms` + `AwaitedReadCount >= 2`; result `null` |
| `Run_FzfFilter_NonexistentPathFallsBackAndLogs` (UPDATED) | BP-1 | `[Telescope] fzf filter failed: {msg}`; result `null` |
| `Run_FzfFinder_GatherOffThread` (NEW) | BP-2 | `fzf hits={count}` UNCHANGED; the gather runs inside `Task.Run` (thread != test thread) |
| `Run_GrepFinder_SyncGetCandidatesThrows` (NEW) | BP-3 | `grep hits={count}` UNCHANGED; sync `GetCandidates("non-empty")` throws |
| 8 rewritten `Run_GrepFinder_*` tests (:3152/:3171/:3185/:3204/:3223/:3247/:3267/:3328) | BP-3 | `grep hits={count}` UNCHANGED (via `GetCandidatesAsync`) |
| `Run_CodeIssuesFinder_GatherAsync` (NEW) | BP-4 | `opened issue: {path} line={line}` UNCHANGED; the TODO scan is async (non-blocking) |
| `Run_GrepFinder_NoSpuriousCancelLog` (NEW) | BP-5 | NO `[Telescope] GrepFinder failed to enumerate:` line on a cancelled gather |
| `Run_QueryDrivenFinderBase_Shared` (NEW) | BP-6 | `grep hits=`/`fzf hits=`/`opened grep:`/`opened fzf:` byte-exact (the ~30+~30 finder tests pin them) |
| `Run_FzfLineMapper_MapDeleted` (NEW) | BP-7 | none (dead code); `Run_FzfLineMapper_Boundary` stays GREEN |
| `Run_FileContentCache_NoToctou` (NEW) | BP-8 | none (cache-internal); `Run_FileContentCache_IOOutsideLock`/`ThreadSafe` stay GREEN |
| `Run_LiteralLineScanner_Direct` (STAYS GREEN) | BP-9 | none (signature change `string[]`→`IReadOnlyList<string>`) |
| `Run_FileFinder_UsesProjectFileCache` (STAYS GREEN) | BP-10 | none (null-forgiving removal) |
| `Run_LogPrefixes_Pinned` (UPDATED) | BP-11 | the 4 remaining prefixes `[NeoVisual]`/`[Telescope]`/`[Hook]`/`[MyExtension]` |
| `Run_WindowFrameAdapter_LinkedIsolation` (NEW) | BP-12 | `[NeoVisual] navigate direction=...` / `navigate activated index=...` UNCHANGED |
| `Run_WindowRect_DeadMirrorsRemoved` (NEW) | BP-13 | `[NeoVisual] navigate direction=...` + `[Telescope] focus target=...` UNCHANGED (shared engine) |
| `Run_RectCoordinate_Adjacency`/`Run_RectCoordinate_GapTo` (DELETED) | BP-13 | n/a (the dead-mirror pins are removed with the mirrors) |
| `Run_Direction_ExplicitMapping` (NEW) | BP-15 | `[NeoVisual] navigate direction={direction.ToChar()}` UNCHANGED |
| `Run_WindowNavigator_NullActive` (NEW) | BP-16 | `[NeoVisual] navigate no-op: no active window` |
| `Run_ExtractFrames_Removed` (NEW) | BP-17 | `[NeoVisual] window frame enumeration failed: {msg}` / `window frame skipped: {msg}` UNCHANGED |

## Known-RED allowlist

- **NONE.** The baseline is all-GREEN (Telescope 319, NeoVisual 236, 44/44 e2e — the review verified both suites). Per-scenario flaky counts start at 0. The e2e scenarios are DEFERRED to the queue (E2E-CR107-1 for Phase 0, E2E-CR107-2 for Phase 1) — they are NOT part of this unit-only gate and must not be reported as regressions if they are not run.

## KEY DECISIONS (the build-agent must not second-guess)

1. **M3 mechanism = `return null`** (not a typed exception) from `FzfFilter.FilterAsync` on timeout/crash. `FzfFinder` treats null as literal-fallback; `TelescopeOverlay.cs:537` (the FileFinder path) coalesces null → the full snapshot display list so `MapBack` keeps the graceful-degradation contract. The whitespace short-circuit (`:175-178`) and the unavailable/spawn-failure/cancellation full-list returns are UNCHANGED.
2. **M4 GrepFinder throw is PAIRED with the test rewrite in the SAME step (BP-3).** The 8 non-empty `GetCandidates("...")` tests become `GetCandidatesAsync(...)`; the empty-query tests stay. `FzfFinder.GetCandidates` already throws for non-empty — GrepFinder matches it.
3. **m10's deletion scope includes `DirectionExtensions.PerpendicularAxis` + the `Axis` enum + `PaneAxis`** (all only referenced by the deleted mirrors). `DirectionExtensions.ToChar` is LIVE and stays. The pinned tests `Run_RectCoordinate_Adjacency`/`GapTo` are deleted; `Run_WindowRect_DeadMirrorsRemoved`/`Run_ExtractFrames_Removed`/`Run_FzfLineMapper_MapDeleted` are reflection-absence checks (the only hermetic way to pin a deletion; single reflection calls, not systemic — Phase 6 m60's reflection reduction targets the systemic cases, not these).
4. **m33's `QueryDrivenFinderBase<THit>` is behavior-neutral EXCEPT one intentional, flagged change: the base's short-circuit predicate is PINNED to `string.IsNullOrWhiteSpace(query)`** (consistent with BP-1's FzfFinder change — the base absorbs it, so it is NOT lost). Consequence: GrepFinder's whitespace-query behavior changes — a whitespace query now returns 0 entries with NO `grep hits=` line (previously `IsNullOrEmpty` at GrepFinder.cs:76 let it through to the scan). This is flagged in BP-6's Verify-with/Fails-if and covered by the deferred `telescope-grep` gate E2E-CR107-1. The derived ctor signatures are byte-identical (the `MyExtensionPackage.cs:104-107` construction + the test ctors must not change). The ~30 `Run_GrepFinder_*` + ~30 `Run_FzfFinder_*` tests pin the byte-exact `grep hits=`/`fzf hits=`/`opened grep:`/`opened fzf:` literals.
5. **n15's deletion is PAIRED with removing the `GlobalKeyboard` assert from `Run_LogPrefixes_Pinned`** (tests/Telescope.Tests/Program.cs:4076). No doc-ref lint impact (the constant is not backticked in any linted doc). The SKILL.md:232-250 "navigation algorithm" section already describes the pre-`GeometricSelectionEngine` pipeline (Phase 8 M6 doc-drift scope) — it is NOT touched in Section A, and the m10 deletion does not trigger the doc-ref lint (the `c.GapTo(...)`/`c.Adjacency(...)` tokens are not PascalCase symbols).
6. **CodeIssuesFinder M4 (BP-4) overrides `GetCandidatesAsync`** for the async gather; the sync `GatherHits` delegates to it so the overlay's sync `GetCandidates()` at `TelescopeOverlay.cs:388` and the existing tests keep working. The overlay's switch to the async gather is Phase 5 m27 — out of Section A scope.

---

# Section B — Build Plan

# Section B — Build Plan + Verification Trace (Phases 2-4: Vim interop, Hook/input/package, Tool-windows)

> **Source plan:** `plans/plan.md` (107-findings code-review-fixes plan) — Section B covers
> **Phase 2 (m1, m6)**, **Phase 3 (M1, m2-m5, m7, m8, n1-n3)**, **Phase 4 (m14-m22, n5-n8)** =
> **25 findings**.
>
> **Lane: unit-only.** Verify-with = unit test NAMES + diagnostic FORMATS only. The e2e gates
> (E2E-CR107-3/4/5) are DEFERRED to the e2e queue — do NOT reference them in Verify-with.
>
> **Known-RED allowlist: NONE** (per `plans/plan.md` §Known-RED allowlist — the baseline is
> all-GREEN: Telescope 319, NeoVisual 236, 44/44 e2e). The verification-agent must NOT flag any
> Section B test as a pre-existing regression.
>
> **Diagnostic contract (from plan.md §Diagnostics):** no NEW harness-asserted diagnostic
> literals. The Section B diagnostic-BEHAVIOR changes are: **M1** (`[NeoVisual] stale-toolwindow
> sentinel active` now fires when the sentinel file exists), **m15** (`solution-explorer select
> file=...` may now succeed where it logged `select none`), **m16** (FocusKeeper stops on
> editor-focus — `solution-explorer select file=` re-assert timing may change), **m18**
> (`text-motion key=... caret=...` may change for w/e/$/j/k near the 4096-char slice end — the
> FORMAT stays byte-identical). All other Section B findings are diagnostic-neutral (literals
> byte-stable). One NEW failure-path literal is added for m17 (C7 precedent, no harness
> dependency — flagged in KEY DECISIONS).

---

## Build Plan

### Phase 2 — Vim interop (m1, m6)

#### BP-1 — m1: fix `VimBufferSubscriptions.Detach` double-decrement (test FIRST)

- **Files:** `tests/NeoVisual.Tests/Program.cs` (add test), `MyExtension/Vim/Utils/VimBufferSubscriptions.cs`
- **Change:** ADD `Run_VimBufferSubscriptions_NoDoubleDecrement` FIRST (RED — the plan's m1
  research correction: the OnBufferClosed-then-Detach double-decrement is UNTESTED). The test:
  two views A/B share one text buffer, both `MarkClosedSubscribed`; `OnBufferClosed(bufferA)`
  (refcount 2→1, returns false); then `Detach(viewA)` must NOT decrement again (refcount stays 1,
  returns false); `Detach(viewB)` drops to 0 (returns true). THEN fix `Detach` (line 63-80): it
  currently decrements unconditionally (`_closedSubscribed.Remove(buffer)` + `DecrementRefCount`),
  so OnBufferClosed-then-Detach double-decrements to 0 while the sibling split view is still
  attached. Fix: track buffers whose refcount was already decremented by `OnBufferClosed` — add a
  `HashSet<object> _closedDecremented`; `OnBufferClosed` adds the buffer to it before
  `DecrementRefCount`; `Detach` removes the buffer from it and SKIPS the decrement when present
  (returns false). The N3 shared-buffer-second-view case (never Closed-subscribed) still
  decrements (the buffer is not in `_closedDecremented`).
- **Verify-with:** `Run_VimBufferSubscriptions_NoDoubleDecrement` (RED before the fix: the
  `Assert.False(subs.Detach(viewA))` fails because the refcount hits 0; GREEN after). Regression
  pins that must STILL pass: `Run_VimBufferSubscriptions_AttachTwoDetachOneKeepsOther`,
  `Run_VimBufferSubscriptions_DetachSharedBufferSecondViewDecrements`,
  `Run_VimBufferSubscriptions_UnsubscribeRemovesClosedAndRefcount`,
  `Run_VimBufferSubscriptions_DetachRemovesMapEntryAtZero`, `Run_VimBufferSubscriptions_NoLeak`,
  `Run_VimBufferSubscriptions_ReattachAfterDetach`, `Run_VimBufferSubscriptions_DetachNonAttachedNoOp`.
  Diagnostic: `vim-mode=...` UNCHANGED (no new literal).
- **Fails-if:** `Run_VimBufferSubscriptions_NoDoubleDecrement` fails at the `Detach(viewA)` step
  (refcount hit 0) → BP-1 is the culprit. Any existing `Run_VimBufferSubscriptions_*` test breaks
  → the `_closedDecremented` bookkeeping is wrong (e.g. the N3 second-view path no longer
  decrements).

#### BP-2 — m6: `VsVimModeSource.Detach`→`UnsubscribeBuffer` uses the cached text buffer

- **Files:** `MyExtension/Vim/Utils/VimModeSource.cs`, `tests/NeoVisual.Tests/Program.cs` (add test)
- **Change:** `Detach` (line 139-151) currently calls `_subscriptions.Detach(view)` (which removes
  the `_bufferToTextBuffer` entry at refcount 0) and THEN `UnsubscribeBuffer(buffer)`, which
  re-resolves the text buffer via reflection (`GetTextBuffer` at line 268) on a possibly-closing
  buffer — a reflection failure leaks the SwitchedMode subscription. Mirror the m13-fixed
  `OnBufferClosed` path (line 182): read the cached value BEFORE the refcount decrement removes
  the entry. Change `Detach` to `_subscriptions.TryGetTextBuffer(buffer, out object? textBuffer)`
  before `_subscriptions.Detach(view)`, and pass the cached `textBuffer` into a new
  `UnsubscribeBuffer(object buffer, object? textBuffer)` overload that calls `RemoveSwitchedMode`
  only when `textBuffer != null` (no reflection re-read).
- **Verify-with:** `Run_VimModeSource_DetachCached` (mirror `Run_VimModeSource_OnBufferClosedUsesCachedBuffer`
  at Program.cs:2047 — a `ClosingVimBuffer` whose `get_VimTextBuffer` throws after the first call;
  populate `_subscriptions` + `SubscribeBuffer` via reflection; drive `Detach(view)`; assert no
  exception escapes AND `_subscribedTextBuffers` no longer contains the text buffer). RED before
  the fix: `GetTextBuffer` throws on the closing buffer → `RemoveSwitchedMode` skipped → the
  `Assert.False(subscribed.Contains(textBuffer))` fails. Regression pin: `Run_VimModeSource_OnBufferClosedUsesCachedBuffer`
  must still pass. Diagnostic: `vim-mode=...` UNCHANGED.
- **Fails-if:** `Run_VimModeSource_DetachCached` throws out of `Detach` or the subscription set
  still contains the text buffer → BP-2 is the culprit (the cached read is missing or ordered
  after the refcount decrement).

### Phase 3 — Hook + input + package (M1, m2-m5, m7, m8, n1-n3)

#### BP-3 — M1: arm the stale-toolwindow sentinel in production

- **Files:** `MyExtension/Input/Utils/StaleToolWindowSentinel.cs`, `MyExtension/Input/InputHandler.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `_sentinelArmed` (InputHandler.cs:82) is only set by the test seam
  `SetSentinelArmedForTest` — never in production, so `RefreshStaleSentinel()` never runs live and
  the FocusGuard leak guard is a false-green. Fix: expose `internal static bool IsConfigured` on
  `StaleToolWindowSentinel` (a static member — it reads the `NEOVISUAL_LOG_DIR` env var directly
  (or the harness-created sentinel file path), NOT the instance `_path` field; read fresh, not
  cached at static-init), and set `_sentinelArmed = StaleToolWindowSentinel.IsConfigured` in
  BOTH InputHandler ctors (main at :145 and test-only at :181). This makes the sentinel arm
  whenever the harness env var is set (production-reachable), restoring the
  `[NeoVisual] stale-toolwindow sentinel active` gate.
- **Verify-with:** `Run_InputHandler_SentinelArmedInProduction` — set `NEOVISUAL_LOG_DIR` to a
  temp dir, construct the test-only ctor, replace `Clock` with a counting lambda, assert
  `IsKeyOfInterest(Keys.H, true, false, false)` reads the clock (sentinel armed). RED before the
  fix: `_sentinelArmed` stays false → `clockReads == 0`. Also UPDATE `Run_InputHandler_NoPerKeySentinelRead`
  to explicitly unset `NEOVISUAL_LOG_DIR` at the start (and restore it) so the disarmed default
  assertion stays hermetic. Diagnostic: `[NeoVisual] stale-toolwindow sentinel active` fires when
  the sentinel file exists (the harness creates it) — the M1 behavior change.
- **Fails-if:** `Run_InputHandler_SentinelArmedInProduction` reports `clockReads == 0` → BP-3 is
  the culprit (the ctor arming is missing). `Run_InputHandler_NoPerKeySentinelRead` fails because
  the env var leaks → the test's env-var guard is missing.

#### BP-4 — m2: single-source the physical-modifier-VK set

- **Files:** `MyExtension/Input/Utils/KeyNames.cs`, `MyExtension/Input/Utils/LeaderSequenceMatcher.cs`, `MyExtension/Input/Utils/KeybindingConfig.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** the physical-modifier-VK set is defined twice and has drifted:
  `LeaderSequenceMatcher.IsModifierKey` (lines 119-125) includes `LWin`/`RWin`;
  `KeybindingConfig.IsPhysicalModifierKey` (lines 252-261) omits them. Fix: add
  `internal static bool IsPhysicalModifierKey(Keys key)` to `KeyNames` (the shared key-mapping
  helper) covering ALL 11 physical modifier VKs (ShiftKey/LShiftKey/RShiftKey/ControlKey/
  LControlKey/RControlKey/Menu/LMenu/RMenu/LWin/RWin); `LeaderSequenceMatcher.IsModifierKey`
  delegates to it; `KeybindingConfig.IsPhysicalModifierKey` delegates to it (or is deleted and the
  call site at :244 uses `KeyNames.IsPhysicalModifierKey`). `KeybindingConfig.ParseLeader` already
  rejects LWin/RWin separately (:243), so adding them to the shared set is behavior-consistent.
- **Verify-with:** `Run_LeaderSequenceMatcher_SingleModifierVkSet` — assert
  `KeyNames.IsPhysicalModifierKey` returns true for all 11 VKs (incl. LWin/RWin — the drift) and
  false for `Keys.A`. Regression pins: `Run_LeaderSequenceMatcher_AllModifiersTransparentDownAndUp`
  (all 11 transparent), `Run_KeybindingConfig_RejectsPhysicalModifierLeader`,
  `Run_Keybinding_PhysicalModifierLeaderRejected` must still pass. Diagnostic: `leader-binding
  executed: ...` UNCHANGED.
- **Fails-if:** `Run_LeaderSequenceMatcher_SingleModifierVkSet` fails on LWin/RWin → BP-4 is the
  culprit (the shared set omits them). A KeybindingConfig leader test fails → the delegation
  changed ParseLeader behavior.

#### BP-5 — m3: resolve `CurrentController` once in `ExitToolWindowInputMode`

- **Files:** `MyExtension/Input/InputHandler.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `ExitToolWindowInputMode` (lines 597-614) resolves `CurrentController` twice —
  `ShouldRouteToolWindowKey(_windowManager.CurrentController)` at :603 then
  `var controller = _windowManager.CurrentController` at :605 (the exact double-resolution m38
  eliminated elsewhere). Fix: `var controller = _windowManager.CurrentController;` once at the top,
  then `if (ShouldRouteToolWindowKey(controller)) { if (controller?.IsInputMode == true) {...} }`.
- **Verify-with:** `Run_InputHandler_ExitToolWindowInputMode_SingleResolution` — force a
  tool-window state (Toolbox → GeneralToolWindowController), `EnterInputMode()`, replace the
  `_routeDecision` seam with `c => { c?.ExitInputMode(); return true; }` (side effect flips the
  mode), invoke `ExitToolWindowInputMode` via reflection, assert it returns true. RED before the
  fix: the second `CurrentController` resolution sees the flipped (non-input) controller →
  returns false. NOTE: the plan.md AC4 name `Run_InputHandler_SingleControllerResolution` is
  already taken by the m38 test (Program.cs:3432) — the m3 test uses the distinct name above.
  Regression pins: `Run_InputHandler_SingleControllerResolution` (m38),
  `Run_TryRouteToolWindowKey_SingleDecision`, `Run_InputHandler_ShouldRouteToolWindowKey_SingleOverload`
  must still pass. Diagnostic: `toolwindow-exit-input` UNCHANGED.
- **Fails-if:** `Run_InputHandler_ExitToolWindowInputMode_SingleResolution` returns false → BP-5
  is the culprit (CurrentController still resolved twice).

#### BP-6 — m4: stop consuming Ctrl/Alt/Win as "transparent" while a leader sequence is pending

- **Files:** `MyExtension/Input/Utils/LeaderSequenceMatcher.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `LeaderSequenceMatcher.HandleKey` (lines 71-74) consumes EVERY modifier key-down as
  transparent while a sequence is pending — a Ctrl+chord/Alt+Tab in that window has its modifier
  swallowed and the chord letter passes through to the editor. Fix: only Shift (ShiftKey/
  LShiftKey/RShiftKey) is transparent (needed for shifted sequence members like `w,|`); a
  Ctrl/Alt/Win key-down while a sequence is pending ABORTS the sequence (clears it) and returns
  `LeaderResult.Abort` (HandleKey returns false → the modifier passes through to VS, so the
  Ctrl+chord works). Update `Run_LeaderSequenceMatcher_AllModifiersTransparentDownAndUp` (line
  2927) to the new contract: only Shift/LShift/RShift are transparent; Ctrl/Alt/Win abort.
- **Verify-with:** `Run_LeaderSequenceMatcher_ModifierChordPassesThrough` — Space, w (prefix
  pending), Ctrl key-down → `LeaderResultKind.Abort` + `!IsActive` (sequence cleared, not
  swallowed); a Shift key-down still returns `Consume` (transparent). RED before the fix: the Ctrl
  key-down returns `Consume` and the sequence stays active. Regression pins:
  `Run_LeaderSequenceMatcher_ModifierChordTransparent` (Shift transparent for `w,|`),
  `Run_LeaderSequenceMatcher_LettersStillAppendAfterModifier`, `Run_LeaderSequenceMatcher_PrefixWaits`,
  `Run_LeaderSequenceMatcher_CaseSensitive` must still pass. Diagnostic: `leader-binding executed:
  ...` UNCHANGED.
- **Fails-if:** `Run_LeaderSequenceMatcher_ModifierChordPassesThrough` sees the Ctrl key-down
  consumed (not Abort) → BP-6 is the culprit. `Run_LeaderSequenceMatcher_ModifierChordTransparent`
  fails → the Shift-transparent path was over-broadened.

#### BP-7 — m5: shift-aware `KeyNames.ToString` for non-letter keys

- **Files:** `MyExtension/Input/Utils/KeyNames.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `KeyNames.ToString(Keys, bool)` (lines 35-43) delegates non-letter keys to the
  shift-insensitive printable map — a binding like `w,}` (Shift+]) silently never fires while
  `w,]` matches both. Fix: make the shift-aware overload map the SHIFTED printable characters:
  `OemCloseBrackets` → `}` (shift) / `]` (unshift), `OemOpenBrackets` → `{` / `[`,
  `OemQuestion` → `?` / `/`, `OemMinus` → `_` / `-`, `Oemplus` → `+` (both, keep current),
  `OemPipe` → `|` (both, keep current). Update `Run_KeyNames_CaseEncodesShift` (line 408) which
  currently asserts `ToString(Keys.OemMinus, true) == "-"` → now `"_"`.
- **Verify-with:** `Run_KeyNames_ShiftAwareNonLetter` — assert `ToString(Keys.OemCloseBrackets,
  true) == "}"`, `ToString(Keys.OemCloseBrackets, false) == "]"`, `ToString(Keys.OemOpenBrackets,
  true) == "{"`, `ToString(Keys.OemQuestion, true) == "?"`, `ToString(Keys.OemMinus, true) == "_"`,
  and a round-trip: bind `w,}` → drive Space, w, Shift+OemCloseBrackets → `Execute` with sequence
  `w,}`. RED before the fix: `ToString(Keys.OemCloseBrackets, true)` returns `]` → the `w,}`
  binding never fires. Regression pins: `Run_KeyNames_PrintableMappings`,
  `Run_KeyNames_RoundTrip_DiagnosticNav` (unshifted `]`/`[`), `Run_KeyNames_RoundTrip_WindowPrefix_SplitRight`
  (`w,|` via Shift+OemPipe) must still pass. Diagnostic: `leader-binding executed: w,}` now fires
  (the m5 behavior change — no literal change).
- **Fails-if:** `Run_KeyNames_ShiftAwareNonLetter` fails on `}`/`{`/`?`/`_` → BP-7 is the culprit.
  `Run_KeyNames_RoundTrip_DiagnosticNav` fails → the unshifted bracket mapping regressed.

#### BP-8 — m7: prune the `PreviewTextCache` (version-keyed, never pruned until CloseView)

- **Files:** `MyExtension/Package/Utils/PreviewTextCache.cs`, `MyExtension/Package/Utils/PreviewEditorHost.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `PreviewTextCache` (lines 11-29) is a version-keyed dictionary never pruned until
  `CloseView` → unbounded growth over a long overlay session on a frequently-edited buffer. Fix:
  bound the cache — add a max capacity (e.g. 8 entries); `Store` evicts the lowest version when
  the capacity is exceeded (the oldest snapshot is the least likely to be re-read). `PreviewEditorHost`
  keeps calling `_textCache.Get/Store/Clear` unchanged (the pruning is internal to the pure cache).
- **Verify-with:** `Run_PreviewEditorHost_CachePruned` — store 10 distinct versions, assert
  `Get` on the 2 oldest returns null (evicted) and `Get` on the most recent returns the text
  (bounded, most-recent retained); `Clear` empties it. RED before the fix: all 10 versions are
  retained (no eviction). Diagnostic: `preview file=...` / `preview tokens=...` UNCHANGED.
- **Fails-if:** `Run_PreviewEditorHost_CachePruned` finds the oldest version still cached → BP-8
  is the culprit (no eviction). A `Get` on a recent version returns null → the eviction policy
  evicted the wrong entry.

#### BP-9 — m8: invalidate the `ErrorListGatherer` 2s TTL on more events

- **Files:** `MyExtension/Package/Utils/ErrorListCacheDecision.cs`, `MyExtension/Package/Utils/ErrorListGatherer.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** the 2s TTL cache (ErrorListGatherer.cs:103-108) is only invalidated by
  `OnBuildDone`/`DocumentSaved` — a build that doesn't raise those events serves stale entries to
  consecutive `],e`/`[,e` presses. Fix: add a pure invalidation policy to `ErrorListCacheDecision`:
  `internal static bool ShouldInvalidateOnEvent(string eventName)` returning true for
  `"build-done"`, `"document-saved"`, `"document-opened"`, `"window-activated"`; false otherwise.
  `ErrorListGatherer.HookEvents` additionally subscribes `DocumentEvents.DocumentOpened` +
  `WindowEvents.WindowActivated` (each calling `Invalidate()`), and the existing handlers call
  `Invalidate()` through the policy.
- **Verify-with:** `Run_ErrorListGatherer_TtlInvalidated` — assert
  `ErrorListCacheDecision.ShouldInvalidateOnEvent` returns true for the four events and false for
  `"selection-changed"`. Regression pin: the existing `ErrorListCacheDecision.IsFresh` tests
  (age < ttl) must still pass. Diagnostic: `diagnostic-nav direction=... severity=... target=...
  line=...` / `diagnostic-nav no-op: ...` UNCHANGED (the invalidation only changes WHEN a fresh
  gather runs, not the emitted literals).
- **Fails-if:** `Run_ErrorListGatherer_TtlInvalidated` fails on any of the four events → BP-9 is
  the culprit (the policy table is incomplete). The `IsFresh` regression pin fails → the TTL
  semantics were changed.

#### BP-10 — n1: delete the dead `GotoDecision` enum

- **Files:** `MyExtension/Package/MyExtensionPackage.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** delete the `GotoDecision` enum (MyExtensionPackage.cs:581-588) — it is never
  referenced (the GotoDispatcher it documented was inlined into `ExecuteGoto` at :472-495).
- **Verify-with:** `Run_GotoDecision_Deleted` — reflection: `typeof(MyExtensionPackage).Assembly
  .GetType("MyExtension.Package.GotoDecision")` must be null. RED before the fix: the type exists
  → the assertion fails. Diagnostic: n/a (dead code removal; `goto-direct finder=... file=...
  line=...` UNCHANGED).
- **Fails-if:** `Run_GotoDecision_Deleted` finds the type → BP-10 is the culprit (the enum was
  not deleted). A build error referencing `GotoDecision` → a residual reference was missed.

#### BP-11 — n2: single-source the duplicated kind-mapping expression in `RoslynGatherers`

- **Files:** `MyExtension/Package/RoslynGatherers.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** the expression `impl is INamedTypeSymbol nts ? nts.TypeKind.ToString() :
  impl.Kind.ToString()` is duplicated at RoslynGatherers.cs:142-144 (GatherImplementations) and
  :204-206 (GatherDefinitions). Fix: extract `internal static string KindName(string typeKind,
  string kind, bool isNamedType) => isNamedType ? typeKind : kind;` and have both call sites use it
  (`var nts = impl as INamedTypeSymbol; KindName(nts?.TypeKind.ToString() ?? string.Empty,
  impl.Kind.ToString(), nts != null)`).
- **Verify-with:** `Run_RoslynGatherers_KindNameShared` — `KindName("Class", "Method", true) ==
  "Class"`, `KindName("Class", "Method", false) == "Method"`, `KindName("", "Method", false) ==
  "Method"`. Regression pins: the existing `Run_*` finder tests that pin the display kind strings
  (implementations/definitions) must still pass. Diagnostic: `implementations gathered count=...` /
  `opened implementation: file=... line=...` UNCHANGED.
- **Fails-if:** `Run_RoslynGatherers_KindNameShared` fails → BP-11 is the culprit (the helper
  logic is wrong). A finder display-kind test fails → a call site was changed incorrectly.

#### BP-12 — n3: collapse the test-only ctor's re-initialized field state

- **Files:** `MyExtension/Input/InputHandler.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** the test-only ctor (InputHandler.cs:181-197) re-initializes ~15 lines of field state
  the main ctor also sets (the two ctors can drift). Fix: extract a shared private
  `InitMatchers(Keys leaderKey, IReadOnlyDictionary<string, Action> leaderBindings,
  IReadOnlyDictionary<string, Action> simpleBindings)` that sets `_leaderMatcher`, `_simpleMatcher`,
  `_routeDecision` (and `_lastSentinelRefresh`); both ctors call it. The test-only ctor keeps only
  its VS-skipping assignments (`_package = null!`, `_vsVim = new VimModeTracker()`, empty binding
  dicts, etc.).
- **Verify-with:** `Run_InputHandler_TestCtor_SharedInit` — construct the test-only ctor, assert
  (via reflection) `_leaderMatcher`, `_simpleMatcher`, `_routeDecision` are all non-null and
  `_leaderMatcher.IsActive` is false. Regression pins: ALL existing InputHandler tests that use
  the test-only ctor (`Run_InputHandler_NoPerKeySentinelRead`,
  `Run_InputHandler_ShouldRouteToolWindowKey_SingleOverload`,
  `Run_InputHandler_ShiftGateUsesPureOverload`, `Run_InputHandler_SingleControllerResolution`,
  `Run_TryRouteToolWindowKey_SingleDecision`, `Run_InputHandler_ExitToolWindowInputMode_SingleResolution`)
  must still pass. Diagnostic: n/a (no literal change).
- **Fails-if:** `Run_InputHandler_TestCtor_SharedInit` finds a null matcher/seam → BP-12 is the
  culprit (the shared init is incomplete). Any existing InputHandler test fails → the ctor
  refactor dropped a field.

### Phase 4 — Tool-windows (m14-m22, n5-n8)

#### BP-13 — m14: invalidate `_cachedFocusedBox` on focus change (the click case)

- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`, `MyExtension/ToolWindows/WindowManager.cs`, `MyExtension/Package/MyExtensionPackage.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** the m47 `_cachedFocusedBox` cache (SolutionExplorerController.cs:224) is invalidated
  only on mode change (`OnModeChanged` :95) — the Esc case is already handled; the CLICK case is
  the gap (a click on the tree while in normal mode leaves the stale search-box cached, so hjkl
  route through it and are swallowed). Fix: key the invalidation on focus change — add
  `internal void InvalidateFocusedBoxCache() => _cachedFocusedBox = null;` to
  `SolutionExplorerController`; add `public event Action? FocusChanged;` to `WindowManager`, fired
  at the end of `OnWindowFocusChanged()`; `MyExtensionPackage` holds the
  `SolutionExplorerController` reference (extract it from the inline `RegisterController` call in
  the "window-manager" init step) and subscribes `FocusChanged += () =>
  solutionExplorerController.InvalidateFocusedBoxCache()`.
- **Verify-with:** `Run_SolutionExplorer_FocusCacheInvalidated` — construct the controller, set
  `_findFocusedTextBox` to a counting lambda returning boxA, `TryMove(Keys.X)` (caches boxA,
  calls==1), call `controller.InvalidateFocusedBoxCache()`, change the lambda to return boxB,
  `TryMove(Keys.X)` → re-resolves (calls==2). RED before the fix: no invalidation method exists
  (compile-RED) and the cache is never cleared. Regression pins:
  `Run_SolutionExplorer_TryMoveCachedAcrossKeys` (consecutive keys still resolve once),
  `Run_SolutionExplorer_TryMoveSingleWalk` must still pass. Diagnostic: `solution-explorer ...`
  UNCHANGED (the fix only stops hjkl being swallowed after a click).
- **Fails-if:** `Run_SolutionExplorer_FocusCacheInvalidated` shows calls==1 after the
  invalidation → BP-13 is the culprit (the cache was not cleared). `Run_SolutionExplorer_TryMoveCachedAcrossKeys`
  fails → the invalidation fires on every key (over-invalidation).

#### BP-14 — m15: set `Expanded = true` before recursing into SolutionFolder nodes

- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `FindFirstProjectNode` (lines 366-384) recurses into `SolutionFolder` nodes without
  setting `Expanded = true` first (M8 fixed this for physical folders only) — `g` logs
  `select none` for a project inside a collapsed solution folder. Fix: make `FindFirstProjectNode`
  `internal static` (the MapChildren precedent) and set `node.UIHierarchyItems.Expanded = true`
  before the `foreach` over a SolutionFolder's children.
- **Verify-with:** `Run_SolutionExplorer_SolutionFolderExpanded` — build a fake solution node →
  collapsed SolutionFolder → project node (FakeUIHierarchyItem/FakeUIHierarchyItems), call
  `SolutionExplorerController.FindFirstProjectNode(solutionNode)`, assert the folder's
  `UIHierarchyItems.Expanded` is true AND the project node is returned. RED before the fix: the
  folder stays collapsed (its children are not enumerated) → the project node is not found.
  Regression pin: `Run_SolutionExplorer_MapChildrenExpandsFolders` must still pass. Diagnostic:
  `solution-explorer select file=...` may now succeed where it logged `select none` (the m15
  behavior change).
- **Fails-if:** `Run_SolutionExplorer_SolutionFolderExpanded` finds the folder not expanded or the
  project node null → BP-14 is the culprit.

#### BP-15 — m16: stop the FocusKeeper when the user moves to the editor

- **Files:** `MyExtension/ToolWindows/Utils/FocusKeeper.cs`, `MyExtension/ToolWindows/SolutionExplorerController.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** the FocusKeeper re-asserts Solution Explorer focus every 100ms for 1.5s and stops
  only on window close — keys typed in that window are routed to the tree and lost when the user
  moves to the editor. Fix: add an `editorFocused` parameter to the pure
  `FocusKeeperSchedule.Decide(bool searchBoxFocused, bool editorFocused, int elapsedMs, int
  escapeAttempts, int durationMs)` — when `editorFocused` is true, return `Decision.Stop`. The
  ticks in `ReturnFocusToTree` (:179-202) and `SelectFirstSourceFile` (:320-331) pass
  `Keyboard.FocusedElement is IWpfTextView` (a shared `TextMotionHelper.IsEditorFocused()` helper
  is acceptable). Update `Run_FocusKeeperSchedule_TruthTable` and
  `Run_FocusKeeperSchedule_StopsAfterMaxEscapeAttempts` to pass `editorFocused: false`.
- **Verify-with:** `Run_FocusKeeper_StopsOnEditorFocus` — `Decide(searchBoxFocused: true,
  editorFocused: true, elapsedMs: 0, escapeAttempts: 0, durationMs: 1500) == Stop` (editor focus
  wins over the escape loop); `Decide(false, false, 0, 0, 1500) == Reassert` (unchanged).
  Regression pins: the updated `Run_FocusKeeperSchedule_TruthTable` /
  `Run_FocusKeeperSchedule_StopsAfterMaxEscapeAttempts`, `Run_FocusKeeper_TickCancelled`,
  `Run_FocusKeeper_ResetAfterDispose` must still pass. Diagnostic: the FocusKeeper stops on
  editor-focus — the `solution-explorer select file=...` re-assert timing may change (the m16
  behavior change; no literal change).
- **Fails-if:** `Run_FocusKeeper_StopsOnEditorFocus` returns non-Stop for `editorFocused: true` →
  BP-15 is the culprit. A FocusKeeperSchedule regression pin fails → the new parameter changed the
  existing decision table.

#### BP-16 — m17: check the `AdviseSelectionEvents` HRESULT

- **Files:** `MyExtension/ToolWindows/WindowManager.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** the ctor (lines 251-253) discards the `AdviseSelectionEvents` HRESULT — a failure
  leaves `_selectionEventsCookie = 0` and no focus-change events ever fire (stale
  `_isToolWindow`/`_type`/`_textInputSurfaceFocused`). Fix: capture the HRESULT; on a negative
  (failed) HRESULT, log `[NeoVisual] selection events advise failed: 0x{hr:X8}` (a NEW
  failure-path literal, C7 `window type probe failed` precedent — no harness dependency) and leave
  `_selectionEventsCookie = 0` (Dispose already guards `!= 0`).
- **Verify-with:** `Run_WindowManager_AdviseSelectionHresult` — add a `FailingMonitorSelection`
  (or parameterize `FakeMonitorSelection`) whose `AdviseSelectionEvents` returns E_FAIL; construct
  the WindowManager under `WithLogPath`; assert the log contains
  `[NeoVisual] selection events advise failed: 0x` and the ctor does not throw. RED before the
  fix: no failure line is logged. Regression pin: `Run_WindowManager_DefaultControllerCache_NoReflection`
  (uses the S_OK FakeMonitorSelection) must still pass. Diagnostic:
  `[NeoVisual] selection events advise failed: 0x{hr:X8}` (new failure-path literal).
- **Fails-if:** `Run_WindowManager_AdviseSelectionHresult` finds no failure line → BP-16 is the
  culprit (the HRESULT is still discarded). The S_OK regression pin fails → the HRESULT check
  misfires on success.

#### BP-17 — m18: single-source the caret-relative slice computation 3× + fix the 4096-char slice hiding text

- **Files:** `MyExtension/ToolWindows/Utils/TextMotionHelper.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** the caret-relative slice computation is duplicated verbatim 3× (WPF TextBox
  :109-118, editor view :132-134, WinForms :153-155), and the 4096-char slice hides text beyond
  the boundary (w/e/$/j/k near the slice end can no-op). Fix: extract
  `internal static (int Start, int Length) ComputeSlice(int caret, int fullLength)` in
  `TextMotionHelper`; all three branches call it. Fix the hiding: the slice extends
  `MotionSliceRadius` (4096) BEFORE the caret and `MotionSliceRadius * 2` (8192) AFTER (the
  forward extension gives w/e/$/j/k room past the old boundary), capped at the buffer bounds. The
  `text-motion key=... caret=... len={fullLength} text='{sample}'` log FORMAT stays byte-identical
  (the sample is the first 30 chars of the FULL buffer; the caret VALUE may change near the slice
  end — the m18 behavior change).
- **Verify-with:** `Run_TextMotionHelper_SliceShared` — assert `ComputeSlice(0, 100) == (0, 100)`
  (whole small buffer), `ComputeSlice(5000, 20000) == (904, 12288)` (4096 before, 8192 after),
  `ComputeSlice(19000, 20000) == (14904, 5096)` (capped at the buffer end). RED before the fix:
  `ComputeSlice` does not exist (compile-RED) and the mid-buffer slice extends only 4096 after.
  Regression pins: `Run_TextMotionHelper_MapMotionDelegatesToDispatcher`,
  `Run_TextMotionHelper_ApplyMotionMovesNavigator`, `Run_TextMotionHelper_SanitizeUsesShared`
  must still pass. Diagnostic: `text-motion key=... caret=...` FORMAT byte-identical; the caret
  value may change for w/e/$/j/k near the slice end (the m18 behavior change).
- **Fails-if:** `Run_TextMotionHelper_SliceShared` fails on the mid-buffer bounds → BP-17 is the
  culprit (the forward extension is missing). A TextMotionHelper regression pin fails → the slice
  refactor changed the motion math.

#### BP-18 — m19: share the FocusKeeper setup between `ReturnFocusToTree` and `SelectFirstSourceFile`

- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** the FocusKeeper setup is duplicated in `ReturnFocusToTree` (:178-202) and
  `SelectFirstSourceFile` (:319-331) — identical `ResetFocusKeeper()` + `_keeper.Run(100ms,
  FocusKeeperDurationMs, ...)` + visibility guard. Fix: extract
  `internal IDisposable StartFocusKeeper(Func<int, bool> tick)` that calls `ResetFocusKeeper()`
  and returns `_keeper.Run(TimeSpan.FromMilliseconds(100), FocusKeeperDurationMs, tick)`; both
  methods call it (each keeps its own tick body — the visibility guard + schedule decision stay in
  the tick).
- **Verify-with:** `Run_FocusKeeper_SetupShared` — construct the controller, call
  `StartFocusKeeper(_ => false)` under the TestScaffold dispatcher, assert the `_focusKeeper`
  field (reflection) is non-null (a keeper started); call it again and assert the field is still
  non-null (the prior keeper was reset, not stacked). RED before the fix: `StartFocusKeeper` does
  not exist (compile-RED). Regression pins: `Run_FocusKeeper_ResetAfterDispose`,
  `Run_FocusKeeper_TickCancelled` must still pass. Diagnostic: n/a (no literal change).
- **Fails-if:** `Run_FocusKeeper_SetupShared` finds `_focusKeeper` null after the call → BP-18 is
  the culprit (the shared setup does not start a keeper). A FocusKeeper regression pin fails →
  the shared setup changed the keeper lifecycle.

#### BP-19 — m20: freeze the `BlockCaretAdornment` brushes + non-hit-testable `_block` + stop the per-keystroke recompute

- **Files:** `Telescope/Overlay/Utils/BlockCaretStyle.cs`, `MyExtension/Adornments/BlockCaretAdornment.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** (1) per-instance unfrozen brushes (BlockCaretAdornment.cs:51-60) — add frozen
  `SolidColorBrush` instances to the shared `BlockCaretStyle` (`public static readonly
  SolidColorBrush WhiteBrush` / `GlyphBrush`, created frozen from `WhiteFill`/`GlyphColor`) and use
  them in the adornment; (2) make `_block` non-hit-testable — set `_block.IsHitTestVisible =
  false` in the ctor so it can't intercept clicks that should place the editor caret; (3) stop the
  per-keystroke `Update()` recompute (:176-177) — cache the last glyph char and only set
  `_glyph.Text` when it changed; freeze `_glyph.FontSize` (compute once, not per caret/layout
  change).
- **Verify-with:** `Run_BlockCaretAdornment_Frozen` — assert `BlockCaretStyle.WhiteBrush.IsFrozen`
  and `BlockCaretStyle.GlyphBrush.IsFrozen` (the pure seam). Regression pin:
  `Run_BlockCaretState_DesiredVsRendered` must still pass (the state model is untouched).
  Diagnostic: `block-caret active=True|False` UNCHANGED (the m20 behavior is rendering-only).
  NOTE: the `_block.IsHitTestVisible` and the per-keystroke recompute guard are WPF-coupled and
  verified by the diagnostic contract + code review (not hermetically unit-testable without a
  hosted view).
- **Fails-if:** `Run_BlockCaretAdornment_Frozen` finds a non-frozen brush → BP-19 is the culprit
  (the frozen brushes are missing). `Run_BlockCaretState_DesiredVsRendered` fails → the state
  model was touched.

#### BP-20 — m21: unify the two controller-resolution paths

- **Files:** `MyExtension/ToolWindows/WindowManager.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `ResolveController` (static, :276-286) and `GetController` (instance-cached,
  :288-313) implement the same "registered wins, else default" logic with divergent semantics.
  Fix: `GetController` delegates to `ResolveController` for the decision, then applies the
  instance cache — `if (_defaultControllers.TryGetValue(type, out var cached)) return cached; var
  created = ResolveController(_controllers, type); if (created != null) _defaultControllers[type]
  = created; return created;`. `ResolveController` stays the single "registered wins, else
  per-type default" decision source.
- **Verify-with:** `Run_WindowManager_SingleResolution` — construct the WindowManager, register a
  controller for Toolbox, assert `GetController(Toolbox)` returns the registered controller
  (registered wins), `GetController(OutputWindow)` returns a `GeneralToolWindowController`
  (per-type default), a second `GetController(OutputWindow)` returns the SAME instance (cached),
  and `ResolveController(_controllers, OutputWindow)` agrees on the type. Regression pins:
  `Run_WindowManager_DefaultControllerFor`, `Run_WindowManager_PerTypeDefaultsDoNotLeakInputMode`,
  `Run_WindowManager_RegisteredControllerWins`, `Run_WindowManager_DefaultControllerCache_NoReflection`
  must still pass. Diagnostic: n/a (no literal change).
- **Fails-if:** `Run_WindowManager_SingleResolution` shows a type mismatch between the two paths →
  BP-20 is the culprit (the unification is incomplete). A WindowManager regression pin fails →
  the delegation changed the caching semantics.

#### BP-21 — m22: wrap `OnWindowFocusChanged`'s `GetProperty`/`GetGuidProperty` in try/catch

- **Files:** `MyExtension/ToolWindows/WindowManager.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `OnWindowFocusChanged` (:357-415) runs inside the `IVsSelectionEvents` COM callback
  with no try/catch around the `GetProperty`/`GetGuidProperty` calls — a disposed frame throws out
  of the COM callback. Fix: wrap the frame-derived state computation (the `GetProperty` +
  `GetGuidProperty` + the two surface computations) in a try/catch that resets the frame-derived
  state (`_isToolWindow = false`, `_type = Unknown`, `_isTextInputType = false`,
  `_textInputSurfaceFocused = false`, `_focusedTextBoxInCurrentToolWindow = false`) on failure.
- **Verify-with:** `Run_WindowManager_FocusChangeTryCatch` — construct the WindowManager, set
  `CurrentWindow` (reflection) to a frame whose `GetProperty(VSFPROPID_Type)` returns S_OK with a
  Tool type and whose `GetGuidProperty` throws (extend `FakeFrame` at Program.cs:4271 or add a
  `ThrowingGuidFrame`), invoke `OnWindowFocusChanged` (via the `SelectionEvents.OnElementValueChanged`
  or reflection), assert no exception escapes. RED before the fix: the `GetGuidProperty` throw
  propagates out of the COM callback. Regression pin: `Run_WindowManager_DefaultControllerCache_NoReflection`
  must still pass. Diagnostic: n/a (the failure is swallowed; no new literal).
- **Fails-if:** `Run_WindowManager_FocusChangeTryCatch` sees an exception escape → BP-21 is the
  culprit (the try/catch is missing or too narrow).

#### BP-22 — n5: delegate the no-box `StyleFocusedSurface` overload

- **Files:** `MyExtension/ToolWindows/Utils/TextMotionHelper.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `StyleFocusedSurface(bool)` (:297-307) and `StyleFocusedSurface(bool, TextBox?)`
  (:311-321) are near-identical. Fix: the no-box overload delegates —
  `public static void StyleFocusedSurface(bool isInputMode) => StyleFocusedSurface(isInputMode,
  FindFocusedTextBox());` (the box overload keeps the shared body).
- **Verify-with:** `Run_StyleFocusedSurface_Delegates` — on the MTA host (no focused box/view),
  `StyleFocusedSurface(false)` and `StyleFocusedSurface(false, null)` are both no-ops that do not
  throw and produce identical results (the delegation is behavior-preserving). Regression pins:
  `Run_TextMotionHelper_MapMotionDelegatesToDispatcher`, `Run_TextMotionHelper_ApplyMotionMovesNavigator`
  must still pass. Diagnostic: n/a (no literal change).
- **Fails-if:** `Run_StyleFocusedSurface_Delegates` throws on the MTA host → BP-22 is the culprit
  (the delegation broke the no-op path).

#### BP-23 — n6: merge the two COM/visual-tree walks per focus change

- **Files:** `MyExtension/ToolWindows/WindowManager.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `OnWindowFocusChanged` runs two separate COM/visual-tree walks per focus change
  (`ComputeTextInputSurfaceFocused` + `ComputeFocusedTextBoxInCurrentToolWindow`, :400-402). Fix:
  merge them into one `ComputeFocusedSurfaceState(out bool textInputSurfaceFocused, out bool
  focusedTextBoxInCurrentToolWindow)` that reads `GetProperty(VSFPROPID_DocView)` ONCE and does
  one visual-tree walk, computing both flags. Introduce a `_findFocusedTextBox` seam
  (`Func<TextBox?>` defaulting to `TextMotionHelper.FindFocusedTextBox`, the
  SolutionExplorerController m5 pattern) so the merged walk is testable.
- **Verify-with:** `Run_WindowManager_SingleWalk` — construct the WindowManager, set
  `CurrentWindow` (reflection) to a counting frame (GetProperty(Type) → Tool, GetGuidProperty →
  Toolbox GUID, GetProperty(DocView) counted), inject a fake box via the `_findFocusedTextBox`
  seam, invoke `OnWindowFocusChanged`, assert `GetProperty(VSFPROPID_DocView)` was called ONCE
  (not twice). RED before the fix: the two walks call it twice. Regression pin:
  `Run_WindowManager_DefaultControllerCache_NoReflection` must still pass. Diagnostic: n/a (no
  literal change).
- **Fails-if:** `Run_WindowManager_SingleWalk` counts two DocView reads → BP-23 is the culprit
  (the walks are not merged).

#### BP-24 — n7: register J/K/D0/D4 in `TextInputToolWindowController`

- **Files:** `MyExtension/ToolWindows/TextInputToolWindowController.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** the text-input controller (lines 39-43) registers A/H/L/I + W/B/E but NOT J/K/D0/D4
  — the Command Window lacks 0/$ that the search box has. Fix: add
  `_actions[Keys.J] = TextMotion(Keys.J); _actions[Keys.K] = TextMotion(Keys.K);
  _actions[Keys.D0] = TextMotion(Keys.D0); _actions[Keys.D4] = TextMotion(Keys.D4);` (the shared
  `TextMotion` wiring; the motions themselves are already mapped by `TextMotionDispatcher.MapKey`).
- **Verify-with:** `Run_TextInput_KeysJKD0D4` — construct
  `TextInputToolWindowController(ToolWindowType.CommandWindow)`, assert `ActionKeys` contains
  J/K/D0/D4 (the registration). Regression pins: `Run_TextInput_StartsInInsertMode`,
  `Run_TextInput_TryMove_UnmappedKeyNotConsumed`, `Run_TextInput_KeysIMapsToInsertStart` must
  still pass. Diagnostic: `text-motion key=... caret=...` / `textinput-enter-input ...` UNCHANGED
  (the Command Window now routes j/k/0/$ through the same shared motion path).
- **Fails-if:** `Run_TextInput_KeysJKD0D4` finds J/K/D0/D4 missing from `ActionKeys` → BP-24 is
  the culprit (the registration is missing).

#### BP-25 — n8: reclassify `ObjectSearchResultsWindow` as non-text-input

- **Files:** `MyExtension/ToolWindows/Utils/ToolWindowTypeResolver.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `IsTextInputType` (line 169) classifies `ObjectSearchResultsWindow` as text-input
  (starts in input mode), unlike FindResults1/2 which m22 removed. Fix: remove
  `case ToolWindowType.ObjectSearchResultsWindow:` from the text-input switch.
- **Verify-with:** `Run_ToolWindowTypeResolver_ObjectSearchNotTextInput` — assert
  `ToolWindowTypeResolver.IsTextInputType(ToolWindowType.ObjectSearchResultsWindow)` is false.
  RED before the fix: it returns true. Regression pins: `Run_ToolWindowTypeResolver_FindResultsNotTextInput`
  must still pass. Diagnostic: n/a (the window now starts in normal mode — no literal change).
- **Fails-if:** `Run_ToolWindowTypeResolver_ObjectSearchNotTextInput` returns true → BP-25 is the
  culprit (the case was not removed).

---

## Verification Trace

| failing test / scenario | implicated steps | expected diagnostic |
|---|---|---|
| `Run_VimBufferSubscriptions_NoDoubleDecrement` | BP-1 | n/a (pure state; `vim-mode=...` unchanged) |
| `Run_VimModeSource_DetachCached` | BP-2 | n/a (`vim-mode=...` unchanged) |
| `Run_InputHandler_SentinelArmedInProduction` | BP-3 | `[NeoVisual] stale-toolwindow sentinel active` fires when the sentinel file exists (M1 behavior change) |
| `Run_LeaderSequenceMatcher_SingleModifierVkSet` | BP-4 | n/a (`leader-binding executed: ...` unchanged) |
| `Run_InputHandler_ExitToolWindowInputMode_SingleResolution` | BP-5 | n/a (`toolwindow-exit-input` unchanged) |
| `Run_LeaderSequenceMatcher_ModifierChordPassesThrough` | BP-6 | n/a (`leader-binding executed: ...` unchanged) |
| `Run_KeyNames_ShiftAwareNonLetter` | BP-7 | n/a (`leader-binding executed: w,}` now fires — no literal change) |
| `Run_PreviewEditorHost_CachePruned` | BP-8 | n/a (`preview file=...` / `preview tokens=...` unchanged) |
| `Run_ErrorListGatherer_TtlInvalidated` | BP-9 | n/a (`diagnostic-nav direction=... severity=... target=... line=...` unchanged) |
| `Run_GotoDecision_Deleted` | BP-10 | n/a (dead enum removed; `goto-direct finder=... file=... line=...` unchanged) |
| `Run_RoslynGatherers_KindNameShared` | BP-11 | n/a (`implementations gathered count=...` / `opened implementation: ...` unchanged) |
| `Run_InputHandler_TestCtor_SharedInit` | BP-12 | n/a |
| `Run_SolutionExplorer_FocusCacheInvalidated` | BP-13 | n/a (`solution-explorer ...` unchanged) |
| `Run_SolutionExplorer_SolutionFolderExpanded` | BP-14 | `solution-explorer select file=...` may now succeed where it logged `select none` (m15 behavior change) |
| `Run_FocusKeeper_StopsOnEditorFocus` | BP-15 | n/a (keeper stops on editor-focus — `solution-explorer select file=` re-assert timing may change, m16 behavior change) |
| `Run_WindowManager_AdviseSelectionHresult` | BP-16 | `[NeoVisual] selection events advise failed: 0x{hr:X8}` (NEW failure-path literal, C7 precedent, no harness dependency) |
| `Run_TextMotionHelper_SliceShared` | BP-17 | `text-motion key=... caret=...` FORMAT byte-identical; caret value may change for w/e/$/j/k near the slice end (m18 behavior change) |
| `Run_FocusKeeper_SetupShared` | BP-18 | n/a |
| `Run_BlockCaretAdornment_Frozen` | BP-19 | `block-caret active=True|False` unchanged |
| `Run_WindowManager_SingleResolution` | BP-20 | n/a |
| `Run_WindowManager_FocusChangeTryCatch` | BP-21 | n/a (failure swallowed) |
| `Run_StyleFocusedSurface_Delegates` | BP-22 | n/a |
| `Run_WindowManager_SingleWalk` | BP-23 | n/a |
| `Run_TextInput_KeysJKD0D4` | BP-24 | n/a (`text-motion key=... caret=...` unchanged) |
| `Run_ToolWindowTypeResolver_ObjectSearchNotTextInput` | BP-25 | n/a |

**Existing tests UPDATED by this plan (must be revised in the same step, else they fail):**
`Run_LeaderSequenceMatcher_AllModifiersTransparentDownAndUp` (BP-6 — only Shift stays
transparent), `Run_KeyNames_CaseEncodesShift` (BP-7 — `ToString(Keys.OemMinus, true)` becomes
`"_"`), `Run_FocusKeeperSchedule_TruthTable` + `Run_FocusKeeperSchedule_StopsAfterMaxEscapeAttempts`
(BP-15 — new `editorFocused` parameter), `Run_InputHandler_NoPerKeySentinelRead` (BP-3 — unset
`NEOVISUAL_LOG_DIR` first).

**Existing tests that must STILL PASS (regression pins, listed per step above):** all
`Run_VimBufferSubscriptions_*`, `Run_VimModeSource_OnBufferClosedUsesCachedBuffer`,
`Run_LeaderSequenceMatcher_*`, `Run_KeyNames_*`, `Run_Keybinding*`, `Run_InputHandler_*`,
`Run_TryRouteToolWindowKey_SingleDecision`, `Run_SolutionExplorer_*`, `Run_WindowManager_*`,
`Run_TextMotionHelper_*`, `Run_TextInput_*`, `Run_ToolWindowTypeResolver_*`, `Run_FocusKeeper_*`,
`Run_BlockCaretState_DesiredVsRendered`.

**Known-RED allowlist: NONE** (per `plans/plan.md` §Known-RED allowlist — the baseline is
all-GREEN; the verification-agent must not flag any Section B test as a pre-existing regression).

---

## KEY DECISIONS (the build-agent must not second-guess)

- **m3 test name:** the plan.md AC4 name `Run_InputHandler_SingleControllerResolution` is already
  taken by the m38 test (Program.cs:3432). The m3 test is named
  `Run_InputHandler_ExitToolWindowInputMode_SingleResolution` (distinct, same single-resolution
  contract).
- **m17 diagnostic:** a NEW failure-path literal `[NeoVisual] selection events advise failed:
  0x{hr:X8}` is added (C7 `window type probe failed` precedent). It is harness-independent (no e2e
  site asserts it) — consistent with the plan's "no NEW harness-asserted literals" rule.
- **m14 wiring:** the focus-change invalidation is wired via a new `WindowManager.FocusChanged`
  event + `SolutionExplorerController.InvalidateFocusedBoxCache()`, subscribed in
  `MyExtensionPackage` (which must hold the controller reference). The Esc case stays handled by
  `OnModeChanged` — do NOT invalidate on every key.
- **m16 contract:** `FocusKeeperSchedule.Decide` gains an `editorFocused` parameter (editor focus
  → Stop). The two existing FocusKeeperSchedule tests are updated to pass `editorFocused: false`.
- **m18 slice:** the slice extends 4096 BEFORE and 8192 AFTER the caret (asymmetric forward
  extension) — the `text-motion key=... caret=...` FORMAT stays byte-identical; only the caret
  VALUE may change near the slice end.
- **m20 scope:** only the frozen brushes are hermetically unit-tested; the `_block.IsHitTestVisible`
  and the per-keystroke recompute guard are WPF-coupled and verified by the `block-caret active=`
  diagnostic contract + code review (no hosted-view unit test).
- **n6 seam:** the merged walk requires a `_findFocusedTextBox` seam in `WindowManager` (the
  SolutionExplorerController m5 pattern) so the single-walk test can inject a fake box.
- **M1 arming:** `StaleToolWindowSentinel.IsConfigured` reads the env var fresh (not cached at
  static-init) so the test can set/unset `NEOVISUAL_LOG_DIR` around construction.

## ASSUMPTIONS

- The `FakeFrame` (Program.cs:4271) is extended (or a `ThrowingGuidFrame` added) for the m22 test;
  a `FailingMonitorSelection` (or a parameterized `FakeMonitorSelection`) is added for the m17
  test; a counting frame is added for the n6 test.
- `FindFirstProjectNode` becomes `internal static` (the `MapChildren` precedent) so the m15 test
  can call it directly.
- `StartFocusKeeper` becomes `internal` so the m19 test can call it directly.
- The `Run_InputHandler_NoPerKeySentinelRead` env-var guard is folded into BP-3 (M1) — it is a
  test-infra consequence of arming the sentinel from the env var.

---

# Section C — Build Plan

# Section C — Build Plan (Phase 5: Overlay + columns + finders) — 32 findings

> **Lane: unit-only (e2e DEFERRED — E2E-CR107-6).** Verify-with = unit test NAMES +
> diagnostic FORMATS only. No e2e scenario references.
>
> **Source:** `docs/reviews/code-review.md` (2026-10-07 refresh), Phase 5 of the
> 107-findings plan (`plans/plan.md`). All 32 Section C findings mapped to BP-n steps.
>
> **Sequencing:** Phase 1 = dead-code deletions (BP-1..BP-12), Phase 2 =
> single-sourcing/merges (BP-13..BP-21), Phase 3 = behavior fixes (BP-22..BP-32).
> Execution is one top-to-bottom pass; the phase headers are the hub's mid-plan
> checkpoints (run the Telescope suite at each boundary).
>
> **Known-RED allowlist:** NONE — the baseline is all-GREEN (Telescope 319, NeoVisual
> 236). Every new test is RED-before / GREEN-after its step.
>
> **Key research corrections baked in (do not second-guess):**
> - m46 is a RENDERING change — `results count={n} selected={m} boxText={len}` may change.
> - m28/m30 — `key=... mode=... handled=...` may change (the shift/Ctrl distinctions).
> - m43 — `[Telescope] filter failed: {msg}` is sanitized (no newline splitting).
> - m37 — the RecentFileHit≡FileHit merge does NOT preserve a dir-cell semantic diff: m47
>   (BP-19) deletes the `projectRoot` param + `DirCell` root-trim, so after both steps the
>   Files and Recent dir cells are BOTH full dirs and `Run_FinderColumns_FilesRecentShared`
>   pins IDENTICAL full-dir behavior for both catalogs; the type-disjointness assertion in
>   `Run_ResultsColumns_Recent_Getters` is deliberately updated (a FileHit now renders in Recent).
> - m41 — the shared column builders must preserve the exact pinned values
>   (`Run_ResultsColumns_*_Catalog/Getters` + `Run_ResultsColumns_MinMaxWidths`).
> - n10 — the DelegatePane merge must preserve the Input/Preview Ids + the PreviewPane chrome.

---

## Phase 1: Dead-code deletions (BP-1..BP-12)

### BP-1 (m25) — delete the dead `TextMotionDispatcher.Handle`

- **Files:**
  - `Telescope/Overlay/Utils/TextMotionDispatcher.cs` — delete `Handle(Key, bool, TextMotionNavigator, out CaretPlacement?)` (lines 181-190).
  - `tests/Telescope.Tests/Program.cs` — rewrite `Run_TextMotionDispatcher_MotionsMapToNavigator`, `Run_TextMotionDispatcher_InsertPlacements`, `Run_TextMotionDispatcher_DollarWithoutShiftNotHandled`, `Run_TextMotionDispatcher_DollarWithShiftLineEnds` to use the surviving `MapKey(Key, bool)` + `Apply(TextMotion, TextMotionNavigator, out CaretPlacement?)` surface; add `Run_TextMotionDispatcher_HandleRemoved`.
- **Change:** delete the method. The surviving surface is `MapKey` + `Apply`. The overlay's `TryPromptMotion`/`HandlePreviewKey` already use `MapKey`+`Apply` (via `PromptMotionRouter.ShouldConsume` + `TextMotionDispatcher.Apply`) — no production caller is affected (verified: `TextMotionDispatcher.Handle(` appears only in `tests/Telescope.Tests/Program.cs`).
- **Verify-with:** `Run_TextMotionDispatcher_HandleRemoved` — compile-enforced (any residual `TextMotionDispatcher.Handle(` reference fails the build). The rewritten `Run_TextMotionDispatcher_*` tests pin the surviving surface: H→Left, L→Right, W→NextWord, B→PrevWord, E→EndWord, J→Down, K→Up, D0→LineStart, G→Top, Shift+G→Bottom, A→InsertAfter/AfterCaret, Shift+A→InsertEnd/End, Shift+I→InsertStart/Start, bare I→not handled, bare D4→not handled.
- **Fails-if:** a residual `TextMotionDispatcher.Handle(` reference fails the build; the rewritten tests fail (the `MapKey`+`Apply` surface does not reproduce the pinned motions).

### BP-2 (m35) — delete the dead `HierarchyWalker.FirstFileEndingWith`

- **Files:**
  - `Telescope/Finders/Utils/HierarchyWalker.cs` — delete `FirstFileEndingWith` (lines 36-46).
  - `tests/Telescope.Tests/Program.cs` — delete `Run_HierarchyWalker_FirstFileEndingWith`; remove the `FirstFileEndingWith` assertion from `Run_HierarchyWalker_EmptyTree`; add `Run_HierarchyWalker_DeadRemoved`.
- **Change:** delete the method (test-only — verified: `FirstFileEndingWith(` appears only at its definition). `EnumerateFiles`/`FirstPathEndingWith`/`FirstPathContaining` survive (the production callers).
- **Verify-with:** `Run_HierarchyWalker_DeadRemoved` — compile-enforced (the method is gone). The surviving `Run_HierarchyWalker_SolutionFolderRecursion`/`_NestedItemRecursion`/`_Dedup`/`_FileExistsFiltering`/`_EmptyTree` pin `EnumerateFiles`; `FirstPathEndingWith`/`FirstPathContaining` are pinned by the MyExtension-side `HierarchyResolver` tests (NeoVisual suite).
- **Fails-if:** a residual `FirstFileEndingWith(` reference fails the build.

### BP-3 (m39) — delete the dead `ColumnVisibilityModel.VisibleColumns`

- **Files:**
  - `Telescope/Overlay/Utils/ResultColumn.cs` — delete `VisibleColumns` (lines 127-128).
  - `tests/Telescope.Tests/Program.cs` — add `Run_ColumnVisibilityModel_DeadRemoved`; update any test referencing `.VisibleColumns`.
- **Change:** delete the property. The overlay's `VisibleColumns()` (TelescopeOverlay.cs:624-629) re-implements the catalog-filter via `VisibleIds` — the production path is unaffected (verified: no production `.VisibleColumns` usage).
- **Verify-with:** `Run_ColumnVisibilityModel_DeadRemoved` — compile-enforced (the property is gone). The surviving `Run_ColumnVisibility_ToggleOff`/`_ToggleOn`/`_OrderStability` pin `VisibleIds`/`Toggle`.
- **Fails-if:** a residual `.VisibleColumns` reference fails the build.

### BP-4 (m40) — delete the dead `ResultColumn.Width`/`WidthChars`

- **Files:**
  - `Telescope/Overlay/Utils/ResultColumn.cs` — delete `Width` (line 52) + `WidthChars` (line 55); delete the `ResultColumnWidth` enum if it becomes unused.
  - `tests/Telescope.Tests/Program.cs` — update `Run_ResultsColumns_WidthKinds` (drop the `c.Width`/`c.WidthChars` assertions); add `Run_ResultColumn_DeadRemoved`.
- **Change:** delete the two properties. `ColumnWidths.Compute` detects the absorber via `MaxWidth == int.MaxValue` (ColumnWidths.cs:79-81) — the Flexible width kind is redundant.
- **Verify-with:** `Run_ResultColumn_DeadRemoved` — compile-enforced (the properties are gone). The surviving `Run_ResultsColumns_*_Catalog`/`_Getters` + `Run_ResultsColumns_MinMaxWidths` pin `Id`/`Header`/`MinWidth`/`MaxWidth`/`Truncation`/`DefaultVisible`/`Getter`.
- **Fails-if:** a residual `.Width`/`.WidthChars` reference fails the build; `Run_ResultsColumns_WidthKinds` still references the deleted members.

### BP-5 (m42) — drop the `ImplMap` defensive entries

- **Files:**
  - `Telescope/Overlay/Utils/KindAbbreviations.cs` — drop the defensive entries (lines 66-88), keeping the 7 realistic + 2 user-literal entries.
  - `tests/Telescope.Tests/Program.cs` — update `Run_KindAbbrev_Implementation_DefensiveUnion` (the defensive entries now render via the ≤4-char `Fallback`); update `Run_KindAbbrev_Implementation_UnionDistinct` (the 30-distinct invariant no longer holds — Array/ArrayType, Dynamic/DynamicType, Pointer/PointerType, Method/FunctionPointer/FunctionPointerType, NamedType/Namespace collide via Fallback); add `Run_KindAbbreviations_NoDefensive`.
- **Change:** drop the ~20 unreachable Roslyn TypeKind/SymbolKind entries. The `Fallback` (lowercase, ≤4 chars) covers them. **NOTE (KEY DECISION):** this changes the defensive outputs (e.g. "Delegate"→"dele" not "del") and breaks the 30-distinct union invariant — the two pinned tests are updated deliberately (the defensive entries are unreachable in production; only the 7 realistic kinds are produced by `RoslynGatherers`).
- **Verify-with:** `Run_KindAbbreviations_NoDefensive` — asserts the defensive entries now render via Fallback (e.g. `Implementation("Delegate") == "dele"`, `Implementation("ErrorType") == "erro"`). The 7 realistic + 2 user-literal entries keep their exact abbreviations (pinned by `Run_KindAbbrev_Implementation_Realistic`).
- **Fails-if:** a defensive entry still maps to its old abbreviation; the realistic entries drift.

### BP-6 (n18) — drop the unused `FzfHit` alias

- **Files:**
  - `Telescope/Overlay/Utils/FinderColumns.cs` — delete `using FzfHit = Telescope.Finders.GrepHit;` (line 6).
  - `tests/Telescope.Tests/Program.cs` — add `Run_FinderColumns_NoUnusedAlias`.
- **Change:** delete the alias. The Grep/Fzf catalogs already use `GrepHit` (post-m25).
- **Verify-with:** `Run_FinderColumns_NoUnusedAlias` — compile-enforced (the alias is gone). `Run_ResultsColumns_Fzf_Catalog`/`_Getters` pin the Fzf catalog.
- **Fails-if:** a residual `FzfHit` reference fails the build.

### BP-7 (n11) — delete the `FocusTargetModel.MapKey` pass-through

- **Files:**
  - `Telescope/Overlay/Utils/FocusTargetModel.cs` — delete `MapKey(Key, bool)` (lines 228-231).
  - `Telescope/Overlay/TelescopeOverlay.cs` — line 1013: `FocusTargetModel.MapKey(e.Key, hasCtrl)` → `FocusTargetModel.ChordDirection(e.Key, hasCtrl)`.
  - `tests/Telescope.Tests/Program.cs` — add `Run_FocusTargetModel_MapKeyRemoved`.
- **Change:** delete the pass-through (identical signature to `ChordDirection`, single caller at TelescopeOverlay.cs:1013 — verified).
- **Verify-with:** `Run_FocusTargetModel_MapKeyRemoved` — compile-enforced (the method is gone). `Run_FocusTargetModel_DirectionTable` pins `ChordDirection`.
- **Fails-if:** a residual `FocusTargetModel.MapKey(` reference fails the build.

### BP-8 (n12) — make `CodeIssuesFinder._fileCache` readonly

- **Files:**
  - `Telescope/Finders/CodeIssuesFinder.cs` — line 36: `private ProjectFileCache _fileCache;` → `private readonly ProjectFileCache _fileCache;`.
  - `tests/Telescope.Tests/Program.cs` — add `Run_CodeIssuesFinder_Readonly`.
- **Change:** mark the field readonly (assigned only in the ctor).
- **Verify-with:** `Run_CodeIssuesFinder_Readonly` — a reflection-free init-only check (or compile-enforced: a readonly field cannot be reassigned outside the ctor). `Run_CodeIssuesFinder_TodoScanOffThread`/`Run_FileContentCache_SharedEntry` pin the finder behavior.
- **Fails-if:** the field is reassigned outside the ctor (compile error).

### BP-9 (n16) — simplify the `ResultsFormatter` seam

- **Files:**
  - `Telescope/Overlay/Utils/ResultsFormatter.cs` — simplify the 2-method static seam.
  - `Telescope/Overlay/TelescopeOverlay.cs` — the two call sites: `ResultsFormatter.ColumnsIdList(VisibleIds())` (line 846) + `ResultsFormatter.RenderedTextLength(_lastRowCells)` (line 847).
  - `tests/Telescope.Tests/Program.cs` — add `Run_ResultsFormatter_Simplified`.
- **Change:** simplify the seam (e.g. inline `ColumnsIdList`'s `string.Join(",", ids)` at the call site, or merge the class into a shared helper). The exact `results columns={ids}` and `boxText={len}` formats are byte-stable.
- **Verify-with:** `Run_ResultsFormatter_Simplified` — the surviving seam (or inlined call) still produces `results columns=access,file` and the `boxText=` length; `Run_ResultsFormatter_RenderedTextLength_*`/`_ColumnsIdList_*` survive (or are consolidated per the simplification).
- **Fails-if:** the `results columns=`/`boxText=` formats drift.

### BP-10 (n17) — fix the `NeoVisualTraceListener.WriteLine`→`Write` double-stamp

- **Files:**
  - `Telescope/Logging/Utils/NeoVisualTraceListener.cs` — line 26: `WriteLine(string? message) => Write(message)`.
  - `tests/Telescope.Tests/Program.cs` — add `Run_NeoVisualTraceListener_SingleStamp`.
- **Change:** `WriteLine` must write the message + newline (a `Debug.WriteLine` produces ONE stamped line); `Write` writes without a newline. The current `WriteLine => Write(message)` drops the newline and can double-stamp when the caller also writes a newline.
- **Verify-with:** `Run_NeoVisualTraceListener_SingleStamp` — a `WriteLine("x")` produces exactly one stamped line in the debug log (via `LogFileWriter.WriteDebug`); a `Write("x")` produces one unstamped fragment.
- **Fails-if:** a `WriteLine` produces two stamps or zero newlines.

### BP-11 (n19) — harden `NeoVisualLog.EnsurePane`'s double-checked locking

- **Files:**
  - `Telescope/Logging/NeoVisualLog.cs` — `EnsurePane` (lines 143-184).
  - `tests/Telescope.Tests/Program.cs` — add `Run_NeoVisualLog_EnsurePane`.
- **Change:** hold `PaneSync` across the pane creation (or use a proper double-checked-locking pattern) so two threads cannot both create the pane. The benign race today: the first check is inside the lock, but the creation happens outside it.
- **Verify-with:** `Run_NeoVisualLog_EnsurePane` — a concurrency smoke test (N threads call `Log`; exactly one pane is created) or a reflection-free seam check that the creation is serialized.
- **Fails-if:** two threads create two panes.

### BP-12 (n20) — fix the `ResultMapper` thread-safety comment

- **Files:**
  - `Telescope/Overlay/Utils/ResultMapper.cs` — lines 22-23 (the R40 comment).
  - `tests/Telescope.Tests/Program.cs` — add `Run_ResultMapper_ThreadSafety`.
- **Change:** correct the comment — the mapper is NOT safe off the UI thread (the `_cachedSnapshot`/`_cachedByDisplay` cache is unsynchronized). Doc-only.
- **Verify-with:** `Run_ResultMapper_ThreadSafety` — the comment no longer claims off-UI-thread safety (a doc-content assertion); `Run_ResultMapper_DuplicateDisplayPreserved`/`_UniqueDisplayMapped`/`_UnknownStringNullPayload`/`_OrderPreserved`/`_OrdinalCase` pin the behavior unchanged.
- **Fails-if:** the comment still claims UI-thread safety.

---

## Phase 2: Single-sourcing / merges (BP-13..BP-21)

### BP-13 (m24) — single-source the WPF `Key`→`OverlayKey` table

- **Files:**
  - `Telescope/Overlay/Utils/Panes/ListPane.cs` — `ListKeyMap.Map` (lines 63-77).
  - `Telescope/Overlay/TelescopeOverlay.cs` — `MapKey` (lines 1251-1268).
  - `Telescope/Overlay/Utils/OverlayKeyMapper.cs` (NEW) — the shared WPF `Key`→`OverlayKey` table.
  - `tests/Telescope.Tests/Program.cs` — add `Run_ListKeyMap_SingleTable`.
- **Change:** extract the shared WPF `Key`→`OverlayKey` table (Escape/Q/Enter/J/K/G(shift)/I/A) into one static method both `ListKeyMap.Map` and `TelescopeOverlay.MapKey` delegate to. The List pane's Up/Down→`Other` pin (the native arrows stay live) is preserved via a parameter or the ListKeyMap's own override.
- **Verify-with:** `Run_ListKeyMap_SingleTable` — `ListKeyMap.Map(J,false)` == the shared table's result == `OverlayKey.J`; the existing `Run_ListKeyMap_ArrowsFallThrough`/`_SelectionGesturesClaimed`/`_MotionsFallThrough` pin the List contract.
- **Fails-if:** the two tables drift (a key maps differently in one surface than the other).

### BP-14 (m36) — consolidate the `FileFinder` dual test seam

- **Files:**
  - `Telescope/Finders/FileFinder.cs` — drop `_testCandidateSource` (lines 26, 56); keep the single `_testEnumerate` seam (lines 27, 57).
  - `tests/Telescope.Tests/Program.cs` — rewrite `Run_FileFinder_EnumeratesCandidates`/`_OpenSelectedCallsOpener`/`_OpenMissingFileIsNoOp`/`_UsesProjectFileCache` to the single-seam ctor; add `Run_FileFinder_SingleSeam`.
- **Change:** remove `_testCandidateSource`; the internal ctor takes one enumerate delegate (`Func<IReadOnlyList<string>>? testEnumerate`) + the opener. `GatherHits` routes through the single seam.
- **Verify-with:** `Run_FileFinder_SingleSeam` — the single-seam ctor enumerates candidates; `Run_FileFinder_UsesProjectFileCache` (the cache serves the enumerate delegate once) survives.
- **Fails-if:** both `_testCandidateSource` and `_testEnumerate` remain; a rewritten test fails to compile.

### BP-15 (m37) — merge `RecentFileHit` into `FileHit`

- **Files:**
  - `Telescope/Finders/RecentFileHit.cs` — delete.
  - `Telescope/Finders/RecentFilesFinder.cs` — `FinderBase<FileHit>`; `new FileHit(p, 0)`.
  - `Telescope/Overlay/Utils/FinderColumns.cs` — the Recent catalog → `FileDirPathColumns<FileHit>`.
  - `tests/Telescope.Tests/Program.cs` — update `Run_ResultsColumns_Recent_Catalog`/`_Getters` + `Run_FinderColumns_FilesRecentShared` to `FileHit`; add `Run_RecentFileHit_Merged`.
- **Change:** delete `RecentFileHit`; `RecentFilesFinder` produces `FileHit`. The Recent catalog getters are typed to `FileHit`. **NOTE (KEY DECISION):** the type-disjointness assertion in `Run_ResultsColumns_Recent_Getters` (a `FileHit` renders EMPTY in Recent) is REMOVED — a `FileHit` now renders in Recent. **NOTE (SUPERSEDED BY BP-19/m47):** the dir-cell semantic diff (Files trims root vs Recent full dir) is NOT preserved — BP-19 deletes the `projectRoot` param + `DirCell` root-trim (FinderColumns.cs:37-68), so after both steps the Files and Recent dir cells are BOTH full dirs, and `Run_FinderColumns_FilesRecentShared` ends up pinning IDENTICAL full-dir behavior for both catalogs.
- **Verify-with:** `Run_RecentFileHit_Merged` — `RecentFilesFinder` produces `FileHit` payloads; the Recent catalog renders them. `Run_FinderColumns_FilesRecentShared` is updated by BP-19 to pin the FULL-dir behavior for BOTH catalogs (no dir-cell diff remains after BP-19).
- **Fails-if:** `RecentFileHit` still exists; the Recent dir cell changes beyond the BP-19 full-dir alignment.

### BP-16 (m41) — single-source the `file`/`line`/`text`/`kind` column literals via shared builders

- **Files:**
  - `Telescope/Overlay/Utils/FinderColumns.cs` — add `FileColumn<THit>`/`LineColumn<THit>`/`TextColumn<THit>`/`KindColumn<THit>` builders; the catalogs use them (the `"file"/"File"/28/6/30/Tail/true` repeats at lines 90,102,114,133,145; `"line"` at 106,120,135,149; `"text"` at 122,137).
  - `tests/Telescope.Tests/Program.cs` — add `Run_FinderColumns_SharedBuilders`.
- **Change:** the repeated column literals collapse into shared builders. The builders must preserve the EXACT pinned values (id/header/width/min/max/truncation/defaultVisible).
- **Verify-with:** `Run_FinderColumns_SharedBuilders` — the shared builders produce the exact pinned columns; `Run_ResultsColumns_*_Catalog`/`_Getters` + `Run_ResultsColumns_MinMaxWidths` survive byte-identically.
- **Fails-if:** any pinned column value (id/header/min/max/truncation/defaultVisible) drifts.

### BP-17 (m44) — simplify the `FilterFailureLog` prefix seam

- **Files:**
  - `Telescope/Logging/Utils/FilterFailureLog.cs` — simplify the only prefixed-return log helper.
  - `Telescope/Overlay/TelescopeOverlay.cs` — line 564 call site (`NeoVisualLog.Log(FilterFailureLog.Format(ex))`).
  - `tests/Telescope.Tests/Program.cs` — add `Run_FilterFailureLog_Simplified`.
- **Change:** simplify the seam (e.g. reduce to a single `Format(Exception)` that returns the sanitized prefixed line, or inline the format at the call site). The `[Telescope] filter failed: {msg}` line stays byte-exact.
- **Verify-with:** `Run_FilterFailureLog_Simplified` — the simplified seam still produces `[Telescope] filter failed: boom`; `Run_FilterFailureLog_Format` survives.
- **Fails-if:** the `filter failed:` literal drifts.

### BP-18 (m45) — make `FinderColumns.ForFinder` data-driven

- **Files:**
  - `Telescope/Overlay/Utils/FinderColumns.cs` — replace the switch (lines 158-171) with a `Dictionary<string, Func<IReadOnlyList<ResultColumn>>>` keyed by the finder `Name` (ordinal).
  - `tests/Telescope.Tests/Program.cs` — add `Run_FinderColumns_DataDriven`.
- **Change:** replace the hardcoded switch with a data table. Unknown → empty catalog.
- **Verify-with:** `Run_FinderColumns_DataDriven` — all 7 names ("Files"/"Recent"/"Issues"/"References"/"Grep"/"Fzf"/"Implementation") resolve; "Nope"/"files" → empty; `Run_ResultsColumns_ForFinder_UnknownName_Empty` survives.
- **Fails-if:** a finder name is missing from the table (the catalog returns empty for a real finder).

### BP-19 (m47) — delete the `projectRoot` param

- **Files:**
  - `Telescope/Overlay/Utils/FinderColumns.cs` — delete `projectRoot` from `ForFinder`/`Files`; delete the `DirCell` root-trim branch (lines 37-68).
  - `tests/Telescope.Tests/Program.cs` — update `Run_ResultsColumns_Files_Getters`/`_DirWithoutRoot`/`Run_FinderColumns_FilesRecentShared` (the Files dir cell is now the FULL directory); add `Run_FinderColumns_ProjectRoot`.
- **Change:** delete the `projectRoot` param (never passed by the overlay — TelescopeOverlay.cs:608 calls `ForFinder(name)`). The Files dir cell is always the full containing directory. **NOTE (KEY DECISION):** this is a user-visible behavior change (the Files dir column shows full paths, not root-relative) — the pinned tests are updated deliberately.
- **Verify-with:** `Run_FinderColumns_ProjectRoot` — compile-enforced (the param is gone); the Files dir cell returns the full directory.
- **Fails-if:** a residual `projectRoot` reference fails the build; the Files dir cell still trims a root.

### BP-20 (m48) — merge the `FocusTargetModel` parallel lists

- **Files:**
  - `Telescope/Overlay/Utils/FocusTargetModel.cs` — merge `_layout` + `_rects` (lines 138-143) into one list.
  - `tests/Telescope.Tests/Program.cs` — add `Run_FocusTargetModel_SingleList`.
- **Change:** merge the parallel `_layout` (KeyValuePair list) + `_rects` (PaneRect list) into a single list (e.g. a `List<KeyValuePair<FocusTarget, PaneRect>>` with the engine iterating the values, or a combined struct list). `SetLayout` no longer maintains two lists.
- **Verify-with:** `Run_FocusTargetModel_SingleList` — the model uses one list (a capability seam or reflection-free check); `Run_FocusTargetModel_DirectionTable`/`Run_FocusTargetModel_IsSingleResolver`/`Run_FocusTarget_CtrlJFromListMovesToInput`/`Run_PaneSelectionSync_ShellRemoved` pin the geometric behavior.
- **Fails-if:** `_layout`/`_rects` desync (a geometric move picks the wrong pane).

### BP-21 (n10) — merge `PromptPane`/`PreviewPane` into a `DelegatePane`

- **Files:**
  - `Telescope/Overlay/Utils/Panes/DelegatePane.cs` (NEW).
  - `Telescope/Overlay/Utils/Panes/PromptPane.cs` — delete.
  - `Telescope/Overlay/Utils/Panes/PreviewPane.cs` — delete.
  - `Telescope/Overlay/TelescopeOverlay.cs` — lines 207, 308: construct `DelegatePane` for Input + Preview.
  - `tests/Telescope.Tests/Program.cs` — add `Run_DelegatePane`.
- **Change:** one `DelegatePane` (Id, content, activate delegate, optional chrome Border) replaces both. The Input pane keeps Id=Input + no chrome; the Preview pane keeps Id=Preview + the chrome Border (the active-pane accent line).
- **Verify-with:** `Run_DelegatePane` — a `DelegatePane(FocusTarget.Input, ...)` reports Id=Input; a `DelegatePane(FocusTarget.Preview, ..., chrome:true)` reports Id=Preview and toggles the chrome brush on Activate/Deactivate; `Run_PaneHost_RegistryOrderPinned` survives.
- **Fails-if:** the Input/Preview Ids or the PreviewPane chrome drift.

---

## Phase 3: Behavior fixes (BP-22..BP-32)

### BP-22 (m23) — `PreviewCaretMap.Line` reuses the cached `LineIndex`

- **Files:**
  - `Telescope/Overlay/Utils/PreviewCaretMap.cs` — add a `Line(LineIndex index, int offset)` overload; the string-based `Line` delegates to it.
  - `Telescope/Overlay/Utils/TextMotionNavigator.cs` — expose the cached `_lineIndex` via an internal `LineIndex` property.
  - `Telescope/Overlay/TelescopeOverlay.cs` — line 910: pass `_previewNavigator.LineIndex`.
  - `tests/Telescope.Tests/Program.cs` — add `Run_PreviewCaretMap_ReusesLineIndex`.
- **Change:** `PreviewCaretMap.Line` no longer rebuilds a full `LineIndex` per preview load — it reuses the `_previewNavigator`'s cached `LineIndex` (built by `SetText`/`SetTextIfChanged`).
- **Verify-with:** `Run_PreviewCaretMap_ReusesLineIndex` — a `LineIndex`-based overload returns the same 1-based line as the string-based `Line`; `Run_PreviewCaretMap_Clamp`/`Run_PreviewCaret_LineOfClampedOffset` survive. Diagnostic: `[Telescope] preview caret=... line=...` UNCHANGED.
- **Fails-if:** a full `LineIndex` is rebuilt per preview load (the navigator's cached index is not reused).

### BP-23 (m26) — scope `VisibilityByFinder` per-instance

- **Files:**
  - `Telescope/Overlay/TelescopeOverlay.cs` — line 97: `private static readonly Dictionary<...> VisibilityByFinder` → instance field.
  - `tests/Telescope.Tests/Program.cs` — add `Run_VisibilityByFinder_Scoped`.
- **Change:** make `VisibilityByFinder` an instance field (per-overlay). **NOTE (KEY DECISION):** the static was intentional (per-open column-choice persistence); scoping per-instance resets the user's column toggles on each open — the finding's leak fix wins. The `results columns=` diagnostic is unaffected.
- **Verify-with:** `Run_VisibilityByFinder_Scoped` — the dictionary is instance-scoped (a reflection-free check that the field is not static, or a capability seam).
- **Fails-if:** the static dictionary is never cleared (the leak persists).

### BP-24 (m27) — make `ShowOverlayAsync` gather async

- **Files:**
  - `Telescope/Overlay/TelescopeOverlay.cs` — line 388: `_candidates = finder.GetCandidates()` → `await finder.GetCandidatesAsync()`.
  - `tests/Telescope.Tests/Program.cs` — add `Run_ShowOverlayAsync_AsyncGather`.
- **Change:** the initial gather runs off the UI thread via `GetCandidatesAsync` (the `IFinder` seam — FinderBase.cs:47). The UI-thread state (mode label, columns, width) is applied after the await.
- **Verify-with:** `Run_ShowOverlayAsync_AsyncGather` — the gather runs off the UI thread (mirrors `Run_CodeIssuesFinder_TodoScanOffThread`'s thread-id assertion). Diagnostic: `[Telescope] open finder=... candidates=...` UNCHANGED.
- **Fails-if:** `finder.GetCandidates()` still runs synchronously on the UI thread.

### BP-25 (m28) — fix the List-pane `a`/`A`/`i`/`I` shift distinction

- **Files:**
  - `Telescope/Overlay/Utils/Panes/ListPane.cs` — `ListKeyMap.Map`: distinguish shift for A/I.
  - `Telescope/Overlay/Utils/OverlayKeyHandler.cs` — add `ShiftA`/`ShiftI` OverlayKey values + the `HandleNormal` cases → `EnterInsertMode(CaretPlacement.End/Start)`.
  - `tests/Telescope.Tests/Program.cs` — add `Run_ListPane_ShiftDistinction`; update `Run_ListKeyMap_SelectionGesturesClaimed`.
- **Change:** on the List pane, `a` (no shift) → after-caret, `A` (shift) → end, `i` (no shift) → current, `I` (shift) → start. Today `ListKeyMap.Map` maps both `a`/`A` to `OverlayKey.A` (→End) and both `i`/`I` to `OverlayKey.I` (→Current). The fix adds shift-aware OverlayKey values (or routes through the `PromptMotionRouter` placement path).
- **Verify-with:** `Run_ListPane_ShiftDistinction` — `ListKeyMap.Map(Key.A, false)` → after-caret placement; `ListKeyMap.Map(Key.A, true)` → end; `ListKeyMap.Map(Key.I, false)` → current; `ListKeyMap.Map(Key.I, true)` → start. Diagnostic: `key=... mode=... handled=...` may change (flagged — E2E-CR107-6).
- **Fails-if:** `a` still inserts at END (the m28 bug).

### BP-26 (m29) — refresh the prompt caret style in `EnterInsert`

- **Files:**
  - `Telescope/Overlay/TelescopeOverlay.cs` — `EnterInsert` (lines 970-986): call `ApplyPromptCaretStyle()` regardless of the `ShouldFocus` guard.
  - `tests/Telescope.Tests/Program.cs` — add `Run_EnterInsert_CaretStyle`.
- **Change:** `EnterInsert` refreshes the prompt caret style (block→line) even when already on the Input pane (the n16 `ShouldFocus` guard skips `FocusPane`, which was the only place the style refreshed).
- **Verify-with:** `Run_EnterInsert_CaretStyle` — after `EnterInsert` on the Input pane, the prompt caret is the insert-mode line caret (via `BlockCaretStyle.ApplyCaretStyle`). Diagnostic: `Focus prompt => ... mode=insert` UNCHANGED.
- **Fails-if:** the block caret persists after `EnterInsert` on Input.

### BP-27 (m30) — check Ctrl in `TryPromptMotion`/`HandlePreviewKey`

- **Files:**
  - `Telescope/Overlay/Utils/PromptMotionRouter.cs` — add a `bool hasCtrl` param to `ShouldConsume`; a Ctrl chord is never a motion.
  - `Telescope/Overlay/TelescopeOverlay.cs` — `TryPromptMotion` (line 1097) + `HandlePreviewKey` (line 1143) pass `hasCtrl`.
  - `tests/Telescope.Tests/Program.cs` — add `Run_PromptMotion_CtrlGuard`; update `Run_PromptMotionRouter_*` for the new param.
- **Change:** Ctrl+W/B/E/H/L (and Ctrl+A/I) are not treated as vim motions — a Ctrl chord is not a motion. `TryPromptMotion`/`HandlePreviewKey` return false when Ctrl is held.
- **Verify-with:** `Run_PromptMotion_CtrlGuard` — `ShouldConsume(Key.W, false, hasCtrl:true, ...)` returns false (not consumed); `ShouldConsume(Key.W, false, hasCtrl:false, ...)` returns true. Diagnostic: `key=... mode=... handled=...` may change (flagged — E2E-CR107-6).
- **Fails-if:** Ctrl+W becomes NextWord.

### BP-28 (m31) — fix the centering math units

- **Files:**
  - `Telescope/Overlay/Utils/OverlayCentering.cs` (NEW pure helper).
  - `Telescope/Overlay/TelescopeOverlay.cs` — lines 433-441: delegate the centering math.
  - `tests/Telescope.Tests/Program.cs` — add `Run_OverlayCentering_Units`.
- **Change:** extract the centering math into a pure helper that converts the physical-pixel rect to DIPs BEFORE subtracting the DIP `Width`/`Height` (today `r.Width - Width` mixes pixels and DIPs), and uses the target monitor's DPI (not `VisualTreeHelper.GetDpi(this)`'s system DPI).
- **Verify-with:** `Run_OverlayCentering_Units` — the helper centers a pixel rect over a DIP window at a given scale (e.g. scale 1.5: rect 1920×1080, window 760×420 → the DIP-converted center). Diagnostic: none (layout-only).
- **Fails-if:** the overlay centers off-target on a non-100% DPI monitor.

### BP-29 (m32) — wrap `ShowPreview` in try/catch

- **Files:**
  - `Telescope/Overlay/TelescopeOverlay.cs` — `ShowPreview` (lines 881-912): wrap the body in try/catch.
  - `tests/Telescope.Tests/Program.cs` — add `Run_ShowPreview_TryCatch`.
- **Change:** a faulting `_previewEditor.Show`/`ApplyCaret` is caught and logged — never crashes the overlay. **NOTE (KEY DECISION):** the defensive line `[Telescope] preview failed: {msg}` is a NEW failure-path literal (consistent with the existing `OnSelected failed:`/`query gather failed:` family) — the hub should confirm it is not a harness-contract literal.
- **Verify-with:** `Run_ShowPreview_TryCatch` — a faulting preview factory is swallowed; the overlay survives. Diagnostic: `[Telescope] preview failed: {msg}` (defensive).
- **Fails-if:** a faulting preview crashes the overlay.

### BP-30 (m43) — sanitize `FilterFailureLog`'s `ex.Message`

- **Files:**
  - `Telescope/Logging/Utils/FilterFailureLog.cs` — line 15: `DiagnosticLog.SanitizeText(ex.Message)`.
  - `tests/Telescope.Tests/Program.cs` — add `Run_FilterFailureLog_Sanitized`.
- **Change:** sanitize `ex.Message` (control chars → spaces) so a newline cannot split the `[Telescope] filter failed: {msg}` line.
- **Verify-with:** `Run_FilterFailureLog_Sanitized` — `Format(new Exception("boom\nnext"))` contains no newline. Diagnostic: `[Telescope] filter failed: {msg}` sanitized (flagged — no harness dependency).
- **Fails-if:** a newline splits the log line.

### BP-31 (m46) — add the `int.MaxValue` absorber to the Files/Recent catalogs

- **Files:**
  - `Telescope/Overlay/Utils/FinderColumns.cs` — the Files `dir` max 40 → int.MaxValue (line 92) and the Recent `dir` max 40 → int.MaxValue (via the shared `FileDirPathColumns`).
  - `tests/Telescope.Tests/Program.cs` — update `Run_ResultsColumns_MinMaxWidths` (Files dir max int.MaxValue + add Recent); add `Run_ColumnWidths_Absorber`.
- **Change:** the Files/Recent `dir` columns become true absorbers (`MaxWidth == int.MaxValue`), so `ColumnWidths.Compute`'s exact-total invariant holds at a wide list (today the widths sum to 560px at a ~1400px list).
- **Verify-with:** `Run_ColumnWidths_Absorber` — at a wide availableWidth (e.g. 1400), the Files/Recent visible sets' assigned widths sum to availableWidth exactly. `Run_ColumnWidths_Compute_FilesCatalog_Pin` (240px) still passes (file=192, dir=48 — unchanged at the narrow width). Diagnostic: `results count={n} selected={m} boxText={len}` may change (flagged — E2E-CR107-6).
- **Fails-if:** the Files/Recent widths sum to 560px at a ~1400px list.

### BP-32 (n9) — avoid the per-move `List<Candidate>` alloc in `GeometricSelectionEngine.SelectTarget`

- **Files:**
  - `Telescope/Overlay/Utils/GeometricSelectionEngine.cs` — line 58: replace the per-move `List<Candidate>` with a two-pass approach (pass 1 finds minGap, pass 2 finds the best within the band, no allocation).
  - `tests/Telescope.Tests/Program.cs` — add `Run_GeometricSelectionEngine_NoAlloc`.
- **Change:** `SelectTarget` no longer allocates a `List<Candidate>` per move. The two-pass approach preserves the exact selection semantics (in-direction → aligned → closest gap → largest adjacency, `>=` last-tie-wins).
- **Verify-with:** `Run_GeometricSelectionEngine_NoAlloc` — a call to `SelectTarget` allocates no per-move list (via `GC.GetAllocatedBytesForCurrentThread()` if available on net472, else a capability seam); `Run_FocusTarget_*` geometric pins survive byte-identically.
- **Fails-if:** a per-move `List<Candidate>` allocation remains; a geometric pin changes.

---

## Verification Trace

| failing test / scenario | implicated steps | expected diagnostic |
|---|---|---|
| `Run_TextMotionDispatcher_HandleRemoved` (compile-RED: `Handle` still exists) | BP-1 | n/a (compile-enforced) |
| `Run_TextMotionDispatcher_MotionsMapToNavigator` / `_InsertPlacements` / `_DollarWithoutShiftNotHandled` / `_DollarWithShiftLineEnds` (rewritten to `MapKey`+`Apply`) | BP-1 | n/a (behavior pin) |
| `Run_HierarchyWalker_DeadRemoved` (compile-RED: `FirstFileEndingWith` still exists) | BP-2 | n/a (compile-enforced) |
| `Run_ColumnVisibilityModel_DeadRemoved` (compile-RED: `VisibleColumns` still exists) | BP-3 | n/a (compile-enforced) |
| `Run_ResultColumn_DeadRemoved` (compile-RED: `Width`/`WidthChars` still exist) | BP-4 | n/a (compile-enforced) |
| `Run_ResultsColumns_WidthKinds` (updated — no `Width`/`WidthChars`) | BP-4 | n/a (behavior pin) |
| `Run_KindAbbreviations_NoDefensive` (RED: a defensive entry still maps to its old abbreviation) | BP-5 | n/a (behavior pin) |
| `Run_KindAbbrev_Implementation_DefensiveUnion` / `_UnionDistinct` (updated — Fallback outputs) | BP-5 | n/a (behavior pin) |
| `Run_FinderColumns_NoUnusedAlias` (compile-RED: `FzfHit` alias still exists) | BP-6 | n/a (compile-enforced) |
| `Run_FocusTargetModel_MapKeyRemoved` (compile-RED: `MapKey` still exists) | BP-7 | n/a (compile-enforced) |
| `Run_FocusTargetModel_DirectionTable` (survives — `ChordDirection` unchanged) | BP-7 | n/a (behavior pin) |
| `Run_CodeIssuesFinder_Readonly` (RED: `_fileCache` not init-only) | BP-8 | n/a (compile/reflection) |
| `Run_ResultsFormatter_Simplified` (RED: the seam is not simplified) | BP-9 | `results columns={ids}` / `results count={n} selected={m} boxText={len}` byte-stable |
| `Run_NeoVisualTraceListener_SingleStamp` (RED: a `WriteLine` double-stamps) | BP-10 | n/a (log-file content) |
| `Run_NeoVisualLog_EnsurePane` (RED: two threads create two panes) | BP-11 | n/a (concurrency) |
| `Run_ResultMapper_ThreadSafety` (RED: the comment still claims UI-thread safety) | BP-12 | n/a (doc-content) |
| `Run_ListKeyMap_SingleTable` (RED: the two tables drift) | BP-13 | n/a (behavior pin) |
| `Run_ListKeyMap_ArrowsFallThrough` / `_SelectionGesturesClaimed` / `_MotionsFallThrough` (survive) | BP-13 | n/a (behavior pin) |
| `Run_FileFinder_SingleSeam` (RED: the dual seam remains) | BP-14 | n/a (compile/behavior) |
| `Run_FileFinder_EnumeratesCandidates` / `_OpenSelectedCallsOpener` / `_OpenMissingFileIsNoOp` / `_UsesProjectFileCache` (rewritten to the single seam) | BP-14 | n/a (behavior pin) |
| `Run_RecentFileHit_Merged` (RED: `RecentFileHit` still exists) | BP-15 | n/a (compile-enforced) |
| `Run_ResultsColumns_Recent_Catalog` / `_Getters` (updated to `FileHit`) | BP-15 | n/a (behavior pin) |
| `Run_FinderColumns_FilesRecentShared` (updated by BP-19 — pins IDENTICAL full-dir behavior for both catalogs; no dir-cell diff remains) | BP-15, BP-19 | n/a (behavior pin) |
| `Run_FinderColumns_SharedBuilders` (RED: a pinned column value drifts) | BP-16 | n/a (behavior pin) |
| `Run_ResultsColumns_*_Catalog` / `_Getters` / `Run_ResultsColumns_MinMaxWidths` (survive byte-identically) | BP-16 | n/a (behavior pin) |
| `Run_FilterFailureLog_Simplified` (RED: the seam is not simplified) | BP-17 | `[Telescope] filter failed: {msg}` byte-exact |
| `Run_FilterFailureLog_Format` (survives) | BP-17 | `[Telescope] filter failed: boom` |
| `Run_FinderColumns_DataDriven` (RED: a finder name is missing from the table) | BP-18 | n/a (behavior pin) |
| `Run_ResultsColumns_ForFinder_UnknownName_Empty` (survives) | BP-18 | n/a (behavior pin) |
| `Run_FinderColumns_ProjectRoot` (compile-RED: `projectRoot` still exists) | BP-19 | n/a (compile-enforced) |
| `Run_ResultsColumns_Files_Getters` / `_DirWithoutRoot` / `Run_FinderColumns_FilesRecentShared` (updated — full dir) | BP-19 | n/a (behavior pin) |
| `Run_FocusTargetModel_SingleList` (RED: the parallel lists remain) | BP-20 | n/a (capability seam) |
| `Run_FocusTargetModel_DirectionTable` / `_IsSingleResolver` / `Run_FocusTarget_CtrlJFromListMovesToInput` / `Run_PaneSelectionSync_ShellRemoved` (survive) | BP-20 | `[Telescope] focus target=Input|List|Preview` unchanged |
| `Run_DelegatePane` (RED: the Input/Preview Ids or the chrome drift) | BP-21 | n/a (behavior pin) |
| `Run_PaneHost_RegistryOrderPinned` (survives) | BP-21 | n/a (behavior pin) |
| `Run_PreviewCaretMap_ReusesLineIndex` (RED: a full `LineIndex` is rebuilt per load) | BP-22 | `[Telescope] preview caret=... line=...` UNCHANGED |
| `Run_PreviewCaretMap_Clamp` / `Run_PreviewCaret_LineOfClampedOffset` (survive) | BP-22 | n/a (behavior pin) |
| `Run_VisibilityByFinder_Scoped` (RED: the dictionary is static) | BP-23 | `results columns={ids}` unchanged |
| `Run_ShowOverlayAsync_AsyncGather` (RED: the gather runs on the UI thread) | BP-24 | `[Telescope] open finder=... candidates=...` UNCHANGED |
| `Run_ListPane_ShiftDistinction` (RED: `a` inserts at END) | BP-25 | `key=... mode=... handled=...` may change (flagged) |
| `Run_ListKeyMap_SelectionGesturesClaimed` (updated for the shift-aware A/I) | BP-25 | n/a (behavior pin) |
| `Run_EnterInsert_CaretStyle` (RED: the block caret persists after `EnterInsert` on Input) | BP-26 | `Focus prompt => ... mode=insert` UNCHANGED |
| `Run_PromptMotion_CtrlGuard` (RED: Ctrl+W becomes NextWord) | BP-27 | `key=... mode=... handled=...` may change (flagged) |
| `Run_PromptMotionRouter_*` (updated for the `hasCtrl` param) | BP-27 | n/a (behavior pin) |
| `Run_OverlayCentering_Units` (RED: the centering mixes pixels and DIPs) | BP-28 | n/a (layout-only) |
| `Run_ShowPreview_TryCatch` (RED: a faulting preview crashes the overlay) | BP-29 | `[Telescope] preview failed: {msg}` (defensive) |
| `Run_FilterFailureLog_Sanitized` (RED: a newline splits the line) | BP-30 | `[Telescope] filter failed: {msg}` sanitized (flagged) |
| `Run_ColumnWidths_Absorber` (RED: the Files/Recent widths sum to 560px at a wide list) | BP-31 | `results count={n} selected={m} boxText={len}` may change (flagged) |
| `Run_ResultsColumns_MinMaxWidths` (updated — Files dir max int.MaxValue + Recent) | BP-31 | n/a (behavior pin) |
| `Run_ColumnWidths_Compute_FilesCatalog_Pin` (survives at 240px) | BP-31 | n/a (behavior pin) |
| `Run_GeometricSelectionEngine_NoAlloc` (RED: a per-move `List<Candidate>` allocation remains) | BP-32 | n/a (allocation) |
| `Run_FocusTarget_*` geometric pins (survive byte-identically) | BP-32 | `[Telescope] focus target=...` / `focus no-op: ...` unchanged |

**Known-RED allowlist (do NOT report as regressions):** NONE — the baseline is all-GREEN
(Telescope 319, NeoVisual 236). The diagnostic-BEHAVIOR changes flagged above
(m28/m30 `key=...`, m43 `filter failed:`, m46 `boxText=`) are INTENDED contract changes
deferred to the e2e queue (E2E-CR107-6), not regressions.

---

# Section D — Build Plan

# Section D — Build Plan (Phases 6-8): Test infra, Harness, Docs + lint

> Part of the 107-findings code-review-fixes plan (`plans/plan.md`). This section covers
> **Phase 6 (Test infra — M5, m49-m65, n21-n25 → BP-D1..BP-D23), Phase 7 (Harness —
> m66-m71 → BP-D24..BP-D29), Phase 8 (Docs + lint — M6, m72-m76 → BP-D30..BP-D35)**.
> Unit-only: Verify-with = unit test NAMES + diagnostic FORMATS. E2E is DEFERRED to the
> queue (E2E-CR107-7) — the harness/doc steps note the queued gates as SECONDARY checks only.
> Known-RED allowlist: NONE (baseline all-GREEN — Telescope 319, NeoVisual 236).
>
> **Sequencing note:** execution is one top-to-bottom pass (BP-D1 → BP-D35). Phase 6
> (BP-D1..BP-D23) is pure test-infra churn and must land first so the Phase 7 harness
> steps (BP-D24..BP-D29) and Phase 8 doc steps (BP-D30..BP-D35) build on a stable,
> deterministic suite. The Phase 6/7 boundary is a natural mid-plan checkpoint: after
> BP-D23 the two unit suites must be all-GREEN before any harness/doc edit.
>
> **Key research corrections baked in (do not second-guess):**
> - **M5 (BP-D1):** drive timeout+grace through the existing `FzfFilter.DelayFactory`
>   seam and assert the grace delay's token is observed, not elapsed time.
> - **m60 (BP-D13):** the reflection-based tests are systemic (~15) — reduce them via
>   internal seams where feasible; a private rename must not break tests when behavior
>   is unchanged.
> - **m66/m67 (BP-D24/BP-D25):** the `editor-view-opened` order-dependence — avoid the
>   already-open-tab paths (the comment at test-e2e.ps1:1457-1459).
> - **m71 (BP-D29):** the `mode=insert` assertion needs the fixed-log-baseline discipline
>   (Reset-LogBaseline / Wait-NewLogLine), not a whole-file presence check.
> - **m62 (BP-D15):** the actual `Assert.True(x == y)` site is `Run_RecentFilesGatherer_Dispose`
>   (NeoVisual.Tests:3872) — plan.md's AC7 test name `Run_KeybindingConfig_Merge_AssertEqual`
>   is a mislabel; do not propagate it.
> - **Doc steps (BP-D30..BP-D35):** must keep the doc-ref + doc-content lints PASS
>   (`pwsh tools/lint/check-doc-refs.ps1` + `pwsh tools/lint/check-doc-content.ps1`).

## Phase 6 — Test infra (BP-D1..BP-D23)

### BP-D1 (M5) — Drive `Run_FzfFilter_GraceDelayToken` through the `DelayFactory` seam
- **Files**: `tests/Telescope.Tests/Program.cs`; `Telescope/Filter/FzfFilter.cs` (only if the seam needs exposure to the test assembly)
- **Change**: Rewrite `Run_FzfFilter_GraceDelayToken` to drive the timeout + grace tasks through the existing `FzfFilter.DelayFactory` seam (controllable timeout + grace tasks). The test injects a factory whose grace task completes on demand and asserts the **grace token is observed** (the filter waits on the grace task's token), NOT elapsed wall-clock time. If the seam is not currently reachable from the test assembly, expose it via an internal ctor/field (InternalsVisibleTo is already set for `tests/Telescope.Tests`). No production behavior change — this is a test-determinism fix only.
- **Verify-with**: unit test `Run_FzfFilter_GraceDelayToken` (rewritten) — asserts the grace delay's token is observed, not elapsed time; no fixed `Thread.Sleep`. Diagnostic: none new (the fzf failure paths already log `[Telescope] fzf filter failed: {msg}` / `[Telescope] fzf filter failed: timeout after {ms}ms` — unchanged).
- **Fails-if**: the test still sleeps a fixed duration; the grace token is never observed; the test is timing-flaky (fails intermittently on a loaded machine).

### BP-D2 (m49) — Make `Run_FzfFilter_NoSpuriousTimeoutOnCancel` deterministic via the DelayFactory seam
- **Files**: `tests/Telescope.Tests/Program.cs`
- **Change**: Replace the fixed `Thread.Sleep(200)` in `Run_FzfFilter_NoSpuriousTimeoutOnCancel` with the `DelayFactory` seam — the timeout task is driven to completion deterministically. Assert the cancel path does NOT log a spurious timeout (the `[Telescope] fzf filter failed: timeout after {ms}ms` line must not fire on cancel).
- **Verify-with**: unit test `Run_FzfFilter_NoSpuriousTimeoutOnCancel` (rewritten) — deterministic, no fixed sleep; asserts the cancel path produces no spurious timeout diagnostic.
- **Fails-if**: the test still uses a fixed `Thread.Sleep(200)`; the cancel path spuriously times out (the timeout diagnostic fires on cancel).

### BP-D3 (m50) — Use `cache.EntryCount` + `cache.LruOrder` instead of the `ReadEntries` reflection helper
- **Files**: `tests/Telescope.Tests/Program.cs`
- **Change**: In `Run_FileContentCache_ThreadSafe`, replace the `ReadEntries` reflection helper with the already-existing `cache.EntryCount` + `cache.LruOrder` members (both already exist on `FileContentCache`). Delete the reflection helper if it becomes unused. No production change.
- **Verify-with**: unit test `Run_FileContentCache_ThreadSafe` (rewritten) — no reflection; asserts the `EntryCount`/`LruOrder` invariants under the N-threads race.
- **Fails-if**: the test still reflects into the cache's private entries; `EntryCount`/`LruOrder` don't exist or behave differently than the reflection read.

### BP-D4 (m51) — Use `TestScaffold.SetCurrentDispatcherAsUiThread()` instead of inline `ThreadHelper.uiThreadDispatcher` reflection
- **Files**: `tests/Telescope.Tests/Program.cs`
- **Change**: In `Run_TelescopeController_DisposeClosesOverlay`, replace the inline `ThreadHelper.uiThreadDispatcher` reflection with the existing `TestScaffold.SetCurrentDispatcherAsUiThread()` helper. Delete the inline reflection block. No production change.
- **Verify-with**: unit test `Run_TelescopeController_DisposeClosesOverlay` (rewritten) — no reflection; the dispose path still closes the overlay (the `[Telescope] overlay closed` diagnostic path is exercised via the controller seam).
- **Fails-if**: the test still reflects into `ThreadHelper`; the helper does not set the current dispatcher (the dispose path throws or the overlay stays open).

### BP-D5 (m52) — Drop the `_emitted` field-type reflection assert in `Run_PaneFailureTracker_Interlocked`
- **Files**: `tests/Telescope.Tests/Program.cs`
- **Change**: Remove the `_emitted` field-type reflection assert from `Run_PaneFailureTracker_Interlocked`; keep the behavioral N-threads race (N threads emit concurrently, exactly one emission wins). No production change.
- **Verify-with**: unit test `Run_PaneFailureTracker_Interlocked` (rewritten) — behavioral only, no reflection; asserts exactly-one emission under the race.
- **Fails-if**: the test still reflects on the `_emitted` field type; the exactly-one-emission behavioral assertion is lost.

### BP-D6 (m53) — Pin the actual expected displays in `Run_GetCandidates_DefaultQuery_MatchesNoArg`
- **Files**: `tests/Telescope.Tests/Program.cs`
- **Change**: Replace the self-referential noArg-vs-emptyArg oracle in `Run_GetCandidates_DefaultQuery_MatchesNoArg` with pinned literal expected display strings (the actual candidate displays for the default query). No production change.
- **Verify-with**: unit test `Run_GetCandidates_DefaultQuery_MatchesNoArg` (rewritten) — asserts the exact pinned display strings, not a noArg-vs-emptyArg comparison.
- **Fails-if**: the test still compares noArg output to emptyArg output (a self-referential oracle that passes even when both are wrong); the pinned displays drift from the real candidate output.

### BP-D7 (m54) — Delete the redundant standalone `Run_ResultsFormatter_RenderedTextLength`
- **Files**: `tests/Telescope.Tests/Program.cs`
- **Change**: Delete the standalone `Run_ResultsFormatter_RenderedTextLength` test — the five `Run_ResultsFormatter_*` tests already pin the rendered-text values. No production change.
- **Verify-with**: unit test deletion — the five `Run_ResultsFormatter_*` tests still pass and pin the rendered-text length/values; no coverage loss.
- **Fails-if**: the five `_*` tests do not actually pin the length (deleting the standalone test loses coverage); a rendered-text regression goes uncaught.

### BP-D8 (m55) — Split the 10-motion `Run_TextMotionDispatcher_MotionsMapToNavigator` into one-per-motion tests
- **Files**: `tests/Telescope.Tests/Program.cs`
- **Change**: Split the single 10-motion `Run_TextMotionDispatcher_MotionsMapToNavigator` into one test per motion (h/l/j/k/w/b/e/0/$/gg/G — the exact 10 the test currently covers), each asserting its own motion → navigator mapping. No production change.
- **Verify-with**: unit tests `Run_TextMotionDispatcher_Motion_<motion>` (10 tests) — each pins one motion's mapping to the navigator; a single-motion failure is isolated.
- **Fails-if**: a single motion failure fails the whole 10-motion test (no isolation); a motion mapping is unpinned (a regression in one motion goes uncaught).

### BP-D9 (m56) — Add a message param to `AssertCol` in `Run_ResultsColumns_MinMaxWidths`
- **Files**: `tests/Telescope.Tests/Program.cs`
- **Change**: Add a message parameter to the local `AssertCol` helper in `Run_ResultsColumns_MinMaxWidths` so a width mismatch reports which column/width failed. No production change.
- **Verify-with**: unit test `Run_ResultsColumns_MinMaxWidths` (extended) — `AssertCol` takes a message; a failure names the column and the expected/actual width.
- **Fails-if**: a width mismatch fails without naming the column (a bare `Assert.Equal` failure with no context).

### BP-D10 (m57) — Replace `task.Wait(3000)` with a bounded poll on `task.IsCompleted`
- **Files**: `tests/Telescope.Tests/Program.cs`
- **Change**: In `Run_FzfFilter_KillRegisteredBeforeWrite`, replace `task.Wait(3000)` with a bounded poll loop on `task.IsCompleted` (a generous deadline + a failure message naming the gate that never opened). No production change.
- **Verify-with**: unit test `Run_FzfFilter_KillRegisteredBeforeWrite` (rewritten) — deterministic; no fixed `Wait(3000)`; the poll completes when the task completes.
- **Fails-if**: the test still uses `task.Wait(3000)`; the poll never completes (the task never completes and the deadline message fires).

### BP-D11 (m58) — `Assert.True(entered.Wait(5s), ...)` in `Run_FileContentCache_IOOutsideLock`
- **Files**: `tests/Telescope.Tests/Program.cs`
- **Change**: Add a message to the `entered.Wait(...)` assertion in `Run_FileContentCache_IOOutsideLock` so a timeout names the gate that never opened. No production change.
- **Verify-with**: unit test `Run_FileContentCache_IOOutsideLock` (extended) — `Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "<message>")`.
- **Fails-if**: the `Wait` times out without a message (a bare `Assert.True` failure with no context); the IO-outside-lock gate never opens.

### BP-D12 (m59) — Add a message parameter to `Assert.Equal`/`Assert.NotEqual` in TestRunner.cs
- **Files**: `tests/TestRunner.cs`
- **Change**: Add an optional `string message = null` parameter to `Assert.Equal`/`Assert.NotEqual` in the shared `TestRunner` (and print it on failure). This is the shared helper both test projects use.
- **Verify-with**: unit test `Run_Assert_Equal_Message` — a failing `Assert.Equal` prints the message.
- **Fails-if**: `Assert.Equal`/`Assert.NotEqual` still lack a message param; the message is not printed on failure.

### BP-D13 (m60) — Reduce the systemic reflection-based testing in NeoVisual.Tests via internal seams
- **Files**: `tests/NeoVisual.Tests/Program.cs` + the production files that need internal seams (`MyExtension/Input/InputHandler.cs`, `MyExtension/ToolWindows/WindowManager.cs`, `MyExtension/ToolWindows/SolutionExplorerController.cs`, `MyExtension/Package/Utils/ErrorListGatherer.cs`, `MyExtension/Package/Utils/RecentFilesGatherer.cs`, `MyExtension/Navigation/WindowNavigator.cs`)
- **Change**: Add internal seams (InternalsVisibleTo is already set for `tests/NeoVisual.Tests`) where feasible and convert the ~15 reflection-based tests (private-field reads on InputHandler/WindowManager/SolutionExplorerController/ErrorListGatherer/RecentFilesGatherer/WindowNavigator) to use the seams. A private rename must not break tests when behavior is unchanged. Prefer the smallest seam that removes the reflection (internal ctor, internal field accessor, or internal method) — mirror the `OverlayKeyHandler`/`TextMotionNavigator` pattern where a pure seam exists.
- **Verify-with**: unit test `Run_NeoVisual_NoReflection` (or the converted tests) — the ~15 tests no longer reflect into private fields; the full NeoVisual suite still passes (236 baseline).
- **Fails-if**: reflection-based tests remain; a private rename breaks tests (proving the reflection coupling is still there); a seam changes production behavior.

### BP-D14 (m61) — Add a malformed-JSON source to `Run_KeybindingConfig_Merge`
- **Files**: `tests/NeoVisual.Tests/Program.cs`
- **Change**: Add a malformed-JSON source to `Run_KeybindingConfig_Merge`; assert the parse failure is logged (the `[NeoVisual]` keybinding-parse failure diagnostic) and the good sources' bindings survive the merge. No production change (the merge already tolerates a bad source — this pins it).
- **Verify-with**: unit test `Run_KeybindingConfig_Merge_MalformedJson` — a malformed source logs the failure and the good sources' bindings survive.
- **Fails-if**: a malformed source kills the whole merge (good bindings lost); the parse failure is not logged.

### BP-D15 (m62) — Convert `Assert.True(docEvents.UnhookCount == 1, ...)` → `Assert.Equal(1, ..., msg)`
- **Files**: `tests/NeoVisual.Tests/Program.cs`
- **Change**: At the actual site `Run_RecentFilesGatherer_Dispose` (NeoVisual.Tests:3872), convert the `Assert.True(docEvents.UnhookCount == 1, ...)` to `Assert.Equal(1, docEvents.UnhookCount, "<msg>")`. **NOTE:** plan.md's AC7 test name `Run_KeybindingConfig_Merge_AssertEqual` is a mislabel — the real site is the RecentFilesGatherer dispose test; do not propagate the mislabel. No production change.
- **Verify-with**: unit test `Run_RecentFilesGatherer_Dispose` (rewritten) — `Assert.Equal(1, docEvents.UnhookCount, "<msg>")`.
- **Fails-if**: the `Assert.True(x == y)` form remains; the mislabeled test name (`Run_KeybindingConfig_Merge_AssertEqual`) is propagated into the suite.

### BP-D16 (m63) — Isolate the `InjectedKeyGuard.Instance` static-singleton usage via a reset seam
- **Files**: `tests/NeoVisual.Tests/Program.cs`
- **Change**: In `Run_ActionTable_SolutionExplorer_HlArrowVk`, isolate the `InjectedKeyGuard.Instance` static-singleton usage via a reset seam (so a prior test's injected state cannot leak into this test). No production change (the reset seam is test-only, or an internal reset method if one is needed).
- **Verify-with**: unit test `Run_InjectedKeyGuard_Isolated` — the test resets the singleton and is order-independent (passes alone and after any other InjectedKeyGuard test).
- **Fails-if**: the test is order-dependent on `InjectedKeyGuard.Instance` state (passes only when run after/before specific tests).

### BP-D17 (m64) — Add an internal FocusKeeper seam (superseded-tick handler) and invoke it directly
- **Files**: `tests/NeoVisual.Tests/Program.cs` + `MyExtension/ToolWindows/Utils/FocusKeeper.cs`
- **Change**: Add an internal seam to `FocusKeeper` that invokes the superseded-tick handler directly; `Run_FocusKeeper_TickCancelled` calls it instead of pumping the 30ms `DispatcherTimer`. The production tick path is unchanged (the seam is an internal entry point the timer's tick also calls).
- **Verify-with**: unit test `Run_FocusKeeper_TickCancelled_Deterministic` — no `DispatcherTimer` pump; the superseded-tick handler is invoked directly and the cancellation is observed.
- **Fails-if**: the test still pumps the 30ms `DispatcherTimer` (timing-flaky); the seam does not exist or bypasses the real tick logic.

### BP-D18 (m65) — Add `Assert.Throws<T>` to TestRunner.cs
- **Files**: `tests/TestRunner.cs`
- **Change**: Add a generic `Assert.Throws<T>(Action)` helper to the shared `TestRunner` that asserts the exact exception type and returns the exception instance.
- **Verify-with**: unit test `Run_Assert_Throws` — a throwing action returns the exception; a non-throwing action fails; a wrong exception type fails.
- **Fails-if**: `Assert.Throws<T>` does not exist; it does not assert the exact type (a derived type passes).

### BP-D19 (n21) — Replace the fixed `Task.Delay(100)` with a bounded poll on the stub's output
- **Files**: `tests/Telescope.Tests/Program.cs`
- **Change**: In `Run_FzfFilter_TimeoutRace`, replace the fixed `Task.Delay(100)` with a bounded poll on the stub's output (a deadline + a failure message). No production change.
- **Verify-with**: unit test `Run_FzfFilter_TimeoutRace_Deterministic` — no fixed `Task.Delay(100)`; polls the stub's output until it appears or the deadline fires.
- **Fails-if**: the test still sleeps a fixed 100ms; the poll never sees the stub output (the deadline message fires).

### BP-D20 (n22) — Add `Run_SetResults_Clamp`
- **Files**: `tests/Telescope.Tests/Program.cs`
- **Change**: Add a test that `SetResults` clamps the selection — `SetResults` already clamps at `OverlayKeyHandler.cs:85-88`; pin the clamp behavior (out-of-range selection → clamped to the valid range). No production change.
- **Verify-with**: unit test `Run_SetResults_Clamp` — `SetResults` clamps an out-of-range selection to the valid range.
- **Fails-if**: `SetResults` does not clamp (the clamp behavior is unpinned and a regression goes uncaught).

### BP-D21 (n23) — Add an internal `MotionForActionKey(Keys)` seam to `ToolWindowControllerBase`
- **Files**: `tests/NeoVisual.Tests/Program.cs` + `MyExtension/ToolWindows/ToolWindowControllerBase.cs`
- **Change**: Add an internal `MotionForActionKey(Keys)` seam to `ToolWindowControllerBase` (the shared action-table → motion mapping); assert the controller's action table maps `I` → InsertStart in `Run_TextInput_KeysIMapsToInsertStart`. No production behavior change (the seam is an internal accessor over the existing action table).
- **Verify-with**: unit test `Run_TextInput_KeysIMapsToInsertStart_Fixed` — `MotionForActionKey(Keys.I)` returns InsertStart.
- **Fails-if**: the seam does not exist; `I` does not map to InsertStart in the action table.

### BP-D22 (n24) — Replace `ActionKeys.Count > 0` with an exact action-key set assertion
- **Files**: `tests/NeoVisual.Tests/Program.cs`
- **Change**: In `Run_TextInput_StartsInInsertMode`, replace the `ActionKeys.Count > 0` presence check with an exact action-key set assertion (the exact `IReadOnlyCollection<Keys>` the controller exposes). No production change.
- **Verify-with**: unit test `Run_TextInput_StartsInInsertMode_Strong` — asserts the exact action-key set, not just `Count > 0`.
- **Fails-if**: the test still only checks `Count > 0` (a wrong-but-nonempty set passes); the exact set is unpinned.

### BP-D23 (n25) — Make `Run_FocusKeeper_ResetAfterDispose` actually dispose
- **Files**: `tests/NeoVisual.Tests/Program.cs`
- **Change**: In `Run_FocusKeeper_ResetAfterDispose`, inject a tracking `IDisposable` into `_focusKeeper`, call `ResetFocusKeeper()`, and assert the injected disposable is disposed AND the keeper field is nulled. No production change.
- **Verify-with**: unit test `Run_FocusKeeper_ResetAfterDispose_Real` — the injected `IDisposable` is disposed and `_focusKeeper` is nulled after reset.
- **Fails-if**: the test does not actually dispose (the injected disposable's `Dispose` is never called); the keeper field is not nulled.

## Phase 7 — Harness (BP-D24..BP-D29)

> Phase 7 is harness-only (PowerShell). Primary Verify-with is the harness code change
> itself + the harness parse/self-check (`pwsh tools/harness/test-e2e.ps1 -SelfCheck` /
> `-List` — no VS boot). The queued e2e gates (E2E-CR107-7) are SECONDARY checks only —
> they are deferred to a capable machine and must NOT be run here.

### BP-D24 (m66) — Replace the `editor-view-opened file=.*GrepProbe\.cs` assert in `explorer-open-searchbox`
- **Files**: `tools/harness/test-e2e.ps1`
- **Change**: Replace the `editor-view-opened file=.*GrepProbe\.cs` assert in the `explorer-open-searchbox` scenario with `Wait-ActiveDocumentMatch` — the already-open-tab path raises no `TextViewCreated` (the m64 pattern at test-e2e.ps1:1457-1461). The assert must not depend on the `[NeoVisual] editor-view-opened file=...` line for a tab that was already open.
- **Verify-with**: harness code change (the assert now uses `Wait-ActiveDocumentMatch`); harness parse check (`pwsh tools/harness/test-e2e.ps1 -SelfCheck` / `-List`). SECONDARY: queued e2e gate E2E-CR107-7.
- **Fails-if**: the assert still depends on `editor-view-opened` for an already-open tab; the scenario is order-dependent (passes only when the tab is freshly opened).

### BP-D25 (m67) — Same for `neovisual-editor-insert`'s `editor-view-opened file=.*Beta\.cs`
- **Files**: `tools/harness/test-e2e.ps1`
- **Change**: Apply the same `Wait-ActiveDocumentMatch` replacement to the `editor-view-opened file=.*Beta\.cs` assert in the `neovisual-editor-insert` scenario (same already-open-tab order-dependence).
- **Verify-with**: harness code change (the assert now uses `Wait-ActiveDocumentMatch`); harness parse check (`pwsh tools/harness/test-e2e.ps1 -SelfCheck` / `-List`). SECONDARY: queued e2e gate E2E-CR107-7.
- **Fails-if**: the assert still depends on `editor-view-opened` for an already-open tab; the scenario is order-dependent.

### BP-D26 (m68) — Replace the fixed bootstrap sleeps with polls
- **Files**: `tools/harness/iterate-telescope.ps1`
- **Change**: Replace the fixed bootstrap sleeps (iterate-telescope.ps1:250, 236, 252) with bounded polls on the expected state (a deadline + a failure message), matching the harness's `Wait-NewLogLine` discipline.
- **Verify-with**: harness code change (no fixed sleeps remain in the bootstrap); harness parse check (`pwsh tools/harness/test-e2e.ps1 -SelfCheck` / `-List`). SECONDARY: queued e2e gate E2E-CR107-7.
- **Fails-if**: fixed sleeps remain in the bootstrap; a poll never reaches the expected state (the deadline message fires).

### BP-D27 (m69) — Re-assert `leader-binding executed:` after each git-bindings retry
- **Files**: `tools/harness/test-e2e.ps1`
- **Change**: In the `neovisual-git-bindings` scenario, re-assert the `[NeoVisual] leader-binding executed: ...` line after each retry (not just the first attempt), so a retry that succeeds is still verified against the leader-binding contract.
- **Verify-with**: harness code change (the `leader-binding executed:` assert is inside the retry loop); harness parse check (`pwsh tools/harness/test-e2e.ps1 -SelfCheck` / `-List`). SECONDARY: queued e2e gate E2E-CR107-7.
- **Fails-if**: a retry succeeds without the `leader-binding executed:` line being re-asserted (the contract is only checked on the first attempt).

### BP-D28 (m70) — Add an explicit empty-box guard before the caret=3 assertions
- **Files**: `tools/harness/test-e2e.ps1`
- **Change**: In the `explorer-searchbox-motions` scenario, add an explicit empty-box guard before the caret=3 assertions (so a non-empty box cannot satisfy them). The `[NeoVisual] text-motion key=... caret=...` assertions must only run against a known-empty search box.
- **Verify-with**: harness code change (the empty-box guard precedes the caret=3 assertions); harness parse check (`pwsh tools/harness/test-e2e.ps1 -SelfCheck` / `-List`). SECONDARY: queued e2e gate E2E-CR107-7.
- **Fails-if**: the caret=3 assertions can be satisfied by a non-empty box (a stale query leaks into the motion assertions).

### BP-D29 (m71) — Apply Reset-LogBaseline/Wait-NewLogLine discipline to the `mode=insert` assertion
- **Files**: `tools/harness/iterate-telescope.ps1`
- **Change**: Apply the fixed-log-baseline discipline (`Reset-LogBaseline` / `Wait-NewLogLine`) to the `mode=insert` assertion in iterate-telescope.ps1 — not a whole-file presence check. A stale `[NeoVisual] vim-mode=Insert` line from an earlier scenario must not satisfy the assertion.
- **Verify-with**: harness code change (the `mode=insert` assertion uses `Reset-LogBaseline`/`Wait-NewLogLine`); harness parse check (`pwsh tools/harness/test-e2e.ps1 -SelfCheck` / `-List`). SECONDARY: queued e2e gate E2E-CR107-7.
- **Fails-if**: the assertion is a whole-file presence check; a stale `mode=insert` line satisfies it (the scenario passes without the mode actually being entered).

## Phase 8 — Docs + lint (BP-D30..BP-D35)

> Phase 8 is docs-only. Verify-with = the doc-ref + doc-content lints PASS
> (`pwsh tools/lint/check-doc-refs.ps1` + `pwsh tools/lint/check-doc-content.ps1`).
> These are the acceptance gates for every doc step.

### BP-D30 (M6) — Fix the spec.md §2.5 + SKILL.md geometric-pipeline attribution
- **Files**: `docs/spec.md` + `.opencode/skills/vs-extension-dev/SKILL.md`
- **Change**: Fix the geometric-pipeline attribution — "FocusTargetModel runs the shared GeometricSelectionEngine, the same engine WindowNavigationEngine delegates to" (the architecture-contract doc drift: the docs must not claim a separate/duplicate selection engine).
- **Verify-with**: doc-ref + doc-content lints PASS (`pwsh tools/lint/check-doc-refs.ps1` + `pwsh tools/lint/check-doc-content.ps1`).
- **Fails-if**: the lints fail; the attribution still names a wrong/duplicate engine (the doc-contract drift remains).

### BP-D31 (m72) — Refresh the progress.md baseline 297/212 → 319/236
- **Files**: `docs/progress.md`
- **Change**: Update the test-count baseline from 297/212 to 319/236 (Telescope 319, NeoVisual 236 — the current all-GREEN counts).
- **Verify-with**: doc-content lint PASS (the baseline assertion, e.g. DOC-66-3, reads the new counts).
- **Fails-if**: the lint's baseline assertion fails; the counts are stale (297/212 still present).

### BP-D32 (m73) — Fix the progress.md In-progress "106-findings is next" → Gap 5
- **Files**: `docs/progress.md`
- **Change**: Fix the In-progress line — "106-findings is next" → Gap 5 (the queue position: the 107-findings plan becomes the FIRST pending item at handoff, before Gap 5).
- **Verify-with**: doc-content lint PASS.
- **Fails-if**: the In-progress line still names "106-findings is next"; the queue position is stale.

### BP-D33 (m74) — Fix the e2e-queue.md status count
- **Files**: `docs/e2e-queue.md`
- **Change**: Fix the status count — "8 gates QUEUED" undercounts; E2E-CR77-1..6 are also QUEUED, so the total QUEUED count is 14.
- **Verify-with**: doc-content lint PASS.
- **Fails-if**: the count still says 8; the E2E-CR77-1..6 gates are not counted in the QUEUED total.

### BP-D34 (m75) — Fix the spec.md + AGENTS.md `preview tokens=` presence-only claims
- **Files**: `docs/spec.md` + `AGENTS.md`
- **Change**: Fix the `[Telescope] preview tokens=...` presence-only claims — m62 dropped that assertion from the harness, so the docs must not claim the harness asserts on `tokens=` (the count reads 0 for both buffer sources at view creation; the semantic coloring is verified by the manual visual pass, not this line).
- **Verify-with**: doc-ref + doc-content lints PASS.
- **Fails-if**: the lints fail; the presence-only `tokens=` claim remains (the docs still say the harness asserts on it).

### BP-D35 (m76) — Fix the agent slice-D lists + the code-review.md summary split
- **Files**: `.opencode/agent/code-review-hub.md` + `.opencode/agent/neovim_review_hub.md` + `docs/reviews/code-review.md`
- **Change**: (1) Fix the agent slice-D lists that name the deleted `SyntaxHighlighter.cs` (the file was retired by the preview migration — remove/replace the reference); (2) fix the code-review.md summary's minor/nit split from 79/22 to 76/25 (matching the findings table's 76 m + 25 n rows; the total stays 107).
- **Verify-with**: doc-ref lint PASS (the `SyntaxHighlighter.cs` refs resolve or are removed — note the check-doc-refs.ps1 `$intentionallyAbsent` entry must be in the EXACT cited path form, e.g. `Overlay/Utils/SyntaxHighlighter.cs`); doc-content lint PASS (the summary split reads 76/25).
- **Fails-if**: the lints fail; the split still says 79/22; `SyntaxHighlighter.cs` is still named in the slice-D lists.

### BP-D36 (doc-ref lint) — Fix the 4 pre-existing unresolved refs in docs/reviews/code-review.md
- **Files**: `docs/reviews/code-review.md`
- **Change**: Fix the 4 unresolved backticked refs the doc-ref lint reports in the review file (verified 2026-10-07 — pre-existing from the review refresh, NOT the handoff): (1) `` `DelegatePane` `` (n10 — a PROPOSED NEW class; the ref resolves once Section C's n10 step creates it, but the review file must not cite a not-yet-existing symbol — reword to "a single DelegatePane" without the backticks, or leave it and rely on the n10 step creating it before the Phase 8 verify); (2) `` `COMException` `` (M2 — a BCL type `System.Runtime.InteropServices.COMException`, not a repo symbol — remove the backticks or qualify it); (3) `` `SyntaxHighlighter.cs` `` (m76 — the deleted file — remove/replace the reference); (4) `` `Down_OnePixelGapAccepted` `` (M7 — the test is `Run_WindowNavigationEngine_Down_OnePixelGapAccepted`; the bare name does not resolve — use the full test name or remove the backticks).
- **Verify-with**: `pwsh tools/lint/check-doc-refs.ps1` PASS (0 unresolved).
- **Fails-if**: the doc-ref lint still reports unresolved refs in `docs/reviews/code-review.md`.

## Verification Trace

> Maps each failing test/scenario to its implicated Build Plan steps and the expected
> diagnostic that proves it. Unit-only: the "failing test" column is the unit test NAME
> (AC7) or the harness/doc gate (AC8/AC9). E2E scenarios are SECONDARY (deferred —
> E2E-CR107-7) and are listed as such.

| failing test / scenario | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_FzfFilter_GraceDelayToken` (AC7) | BP-D1 | grace token observed, not elapsed time; no fixed sleep |
| `Run_FzfFilter_NoSpuriousTimeoutOnCancel` (AC7) | BP-D2 | no spurious `[Telescope] fzf filter failed: timeout after {ms}ms` on cancel |
| `Run_FileContentCache_ThreadSafe` (AC7) | BP-D3 | `cache.EntryCount`/`cache.LruOrder` invariants; no reflection |
| `Run_TelescopeController_DisposeClosesOverlay` (AC7) | BP-D4 | dispose closes the overlay via `TestScaffold.SetCurrentDispatcherAsUiThread()`; no reflection |
| `Run_PaneFailureTracker_Interlocked` (AC7) | BP-D5 | exactly-one emission under the N-threads race; no `_emitted` reflection |
| `Run_GetCandidates_DefaultQuery_MatchesNoArg` (AC7) | BP-D6 | pinned literal display strings (no self-referential oracle) |
| `Run_ResultsFormatter_RenderedTextLength` (AC7) | BP-D7 | deleted; the five `Run_ResultsFormatter_*` tests pin the values |
| `Run_TextMotionDispatcher_MotionsMapToNavigator` (AC7) | BP-D8 | 10 one-per-motion tests, each pinning one motion → navigator mapping |
| `Run_ResultsColumns_MinMaxWidths` (AC7) | BP-D9 | `AssertCol` failure names the column + expected/actual width |
| `Run_FzfFilter_KillRegisteredBeforeWrite` (AC7) | BP-D10 | bounded poll on `task.IsCompleted`; no `Wait(3000)` |
| `Run_FileContentCache_IOOutsideLock` (AC7) | BP-D11 | `Assert.True(entered.Wait(5s), "<msg>")` |
| `Run_Assert_Equal_Message` (AC7) | BP-D12 | `Assert.Equal`/`Assert.NotEqual` print the message on failure |
| `Run_NeoVisual_NoReflection` (AC7) | BP-D13 | ~15 reflection-based tests converted to internal seams; suite still 236 GREEN |
| `Run_KeybindingConfig_Merge_MalformedJson` (AC7) | BP-D14 | malformed source logs the parse failure; good sources' bindings survive |
| `Run_RecentFilesGatherer_Dispose` (AC7 — the real m62 site; plan.md's `Run_KeybindingConfig_Merge_AssertEqual` is a mislabel) | BP-D15 | `Assert.Equal(1, docEvents.UnhookCount, "<msg>")` |
| `Run_InjectedKeyGuard_Isolated` (AC7) | BP-D16 | `InjectedKeyGuard.Instance` reset seam; order-independent |
| `Run_FocusKeeper_TickCancelled` (AC7) | BP-D17 | superseded-tick handler invoked directly; no 30ms DispatcherTimer pump |
| `Run_Assert_Throws` (AC7) | BP-D18 | `Assert.Throws<T>` returns the exact exception type |
| `Run_FzfFilter_TimeoutRace` (AC7) | BP-D19 | bounded poll on the stub's output; no fixed `Task.Delay(100)` |
| `Run_SetResults_Clamp` (AC7) | BP-D20 | `SetResults` clamps out-of-range selection (OverlayKeyHandler.cs:85-88) |
| `Run_TextInput_KeysIMapsToInsertStart` (AC7) | BP-D21 | `MotionForActionKey(Keys.I)` returns InsertStart |
| `Run_TextInput_StartsInInsertMode` (AC7) | BP-D22 | exact action-key set asserted (not `Count > 0`) |
| `Run_FocusKeeper_ResetAfterDispose` (AC7) | BP-D23 | injected `IDisposable` disposed + `_focusKeeper` nulled |
| `explorer-open-searchbox` (AC8 — harness) | BP-D24 | `Wait-ActiveDocumentMatch` replaces the `editor-view-opened file=.*GrepProbe\.cs` assert (order-independent); SECONDARY: E2E-CR107-7 |
| `neovisual-editor-insert` (AC8 — harness) | BP-D25 | `Wait-ActiveDocumentMatch` replaces the `editor-view-opened file=.*Beta\.cs` assert (order-independent); SECONDARY: E2E-CR107-7 |
| iterate-telescope bootstrap (AC8 — harness) | BP-D26 | fixed bootstrap sleeps (iterate-telescope.ps1:250,236,252) replaced with polls; SECONDARY: E2E-CR107-7 |
| `neovisual-git-bindings` (AC8 — harness) | BP-D27 | `[NeoVisual] leader-binding executed: ...` re-asserted after each retry; SECONDARY: E2E-CR107-7 |
| `explorer-searchbox-motions` (AC8 — harness) | BP-D28 | explicit empty-box guard before the caret=3 assertions; SECONDARY: E2E-CR107-7 |
| iterate-telescope `mode=insert` (AC8 — harness) | BP-D29 | `Reset-LogBaseline`/`Wait-NewLogLine` discipline (no whole-file presence check); SECONDARY: E2E-CR107-7 |
| doc-ref lint (AC9) | BP-D30, BP-D34, BP-D35 | `pwsh tools/lint/check-doc-refs.ps1` PASS |
| doc-content lint (AC9) | BP-D31, BP-D32, BP-D33, BP-D34, BP-D35 | `pwsh tools/lint/check-doc-content.ps1` PASS (baseline 319/236, Gap 5, e2e-queue 14 QUEUED, split 76/25) |

### Known-RED allowlist

- **NONE.** The baseline is all-GREEN (Telescope 319, NeoVisual 236, 44/44 e2e — the
  review verified both suites). Per-scenario flaky counts start at 0. The verification-
  agent must NOT flag any of the above as a pre-existing regression.

