# Plan — Code review fixes (106 findings, incl. nits)

> **Lane: feature (unit-only, e2e DEFERRED).** The e2e scenarios are QUEUED in
> `e2e-queue.md` (status QUEUED) — created/executed on a capable machine after this
> plan is GREEN (the user's 2026-10-06 instruction: "we are not on e2e capable machine
> so defer those to queue for later"). RED is proven at the unit level only. M-M7: this
> plan has a small set of diagnostic-BEHAVIOR changes (M2 fzf hit order, M5 leak fix,
> M7 nav target, m5 log frequency, m8 spurious-timeout, m46 placement caret) — each is
> flagged with its harness site; no NEW diagnostic literals.
>
> **Source:** `docs/reviews/code-review.md` (2026-10-06 refresh). The review's summary
> says "77 findings: 0 critical, 12 major, 45 minor, 20 nit", but its OWN findings table
> lists **106 rows (12 M + 74 m + 20 n)** — the summary count is internally inconsistent
> with the table. This plan covers **ALL 106 table rows** (the user asked for "all problem
> that were found in review even nits"). The count discrepancy itself is a doc-drift item
> (fixed in the m71 doc refresh).
>
> **Research (3 subagents, 2026-10-06):** `trailmark-recon` structural digest (graph 2735
> nodes / 1108 proxies = 40.5%; all 7 question groups CONFIRMED; 1 correction — `MapChildren`
> lives in `SolutionExplorerController.cs:385-422`, not the builder, and never sets
> `Expanded` on child folders); `arch-auditor` change-area analysis (8 clusters — verdicts +
> corrections below); `feature-researcher` native-VS/fzf reuse (M5 Got/LostAggregateFocus,
> M8 no native search-box API, M12 ShowDialog/JTF re-entrancy, M2 one `--filter` batch,
> M1/M3 VSTHRD002).
>
> **Key research corrections baked in:**
> - **M1 is sound but the seam is narrow:** `GrepFinder.GetCandidates` (GrepFinder.cs:125-135)
>   runs `Task.Run(...).GetAwaiter().GetResult()` — the caller blocks on the UI thread
>   (chain: `TelescopeOverlay.cs:511` → `FinderBase.cs:47-48` `Task.FromResult(GetCandidates)`).
>   Fix: OVERRIDE `GetCandidatesAsync` in `GrepFinder` to `await Task.Run(...)` the pure
>   `ScanFile` loop (no blocking); keep the DTE enumeration + `EnsureSolutionCache` on the
>   UI thread (`:108` assert); the `_queryGeneration` guard (`:507,514`) already discards
>   stale results. The empty-query path still calls `WarmContentCache` on the UI thread —
>   handled by M3.
> - **M2 is sound but needs care:** one fzf subprocess per file per keystroke
>   (`FzfFinder.cs:125-151`). Fix: batch ALL files' lines into ONE `fzf --filter` call
>   (fzf ranks across all candidates on stdin — feature-researcher verified) and map the
>   ranked output back to (file, line) via a boundary-aware `FzfLineMapper` wrapper
>   (the existing `FzfLineMapper.Map` is per-file). NOTE: global ranking CHANGES the hit
>   ORDER (a diagnostic-behavior change — the `telescope-fzf` open-hit assertion may need
>   updating at the e2e gate). `--listen` is the wrong tool (interactive-only, needs a TTY).
> - **M3:** `WarmContentCache` (GrepFinder.cs:152-182 / FzfFinder.cs:211-241) reads ALL
>   project files on the UI thread at open. Fix: DROP the eager warm-up — the first gather
>   populates the cache lazily (the gather runs off-thread after M1/M2). This also resolves
>   m24 (the byte-identical `WarmContentCache`/`WarmFile` in both finders are deleted, not
>   merged).
> - **M4 (HIGH-RISK):** the review recommends extracting the pure geometric engine into a
>   dependency-free shared class (it claims "MyExtension already references Telescope — the
>   'NOT shared' comment is wrong"). The arch-auditor VERIFIED the csproj direction
>   (`MyExtension/MyExtension.csproj:32` references `Telescope.csproj`; Telescope does NOT
>   reference MyExtension) — so a shared engine in Telescope referenced by
>   `MyExtension.Navigation` is CYCLE-FREE. But the review's "comment is wrong" claim is
>   itself a misreading (`FocusTargetModel.cs:57` says "Telescope does not reference
>   MyExtension", which is factually true). DECISION: **shared extraction** (the review's
>   recommendation — the biggest architecture win), sequenced AFTER M7 (so the shared engine
>   has the converged Down semantics: both surfaces gap>=0), parameterized by
>   (allowNegativeGap, divide). The ~30 `Run_FocusTarget_*` + ~12 `Run_WindowNavigationEngine_*`
>   tests stay GREEN (they pin the behavior). FALLBACK (documented): the ~10-line
>   direction→target table for the fixed 3-pane layout (the prior 34-findings plan's m6
>   attempt failed on test churn — the tests pin the full pipeline with synthetic rects).
> - **M5 needs-correction:** gating `OwnsKeyboard` on `!IsEditorFocused` is WRONG — the
>   Command Window's own `IWpfTextView` also sets `IsEditorFocused` (the leak is
>   `WindowManager.cs:122-123` fallback can't distinguish). Fix: invalidate
>   `_textInputSurfaceFocused` in the focus-change path keyed on the focused view's
>   IDENTITY (the Got/LostAggregateFocus pattern VimModeTracker uses), not `IsEditorFocused`.
> - **m4 needs-correction:** the CodeIssuesFinder TODO scan is pure I/O (off-thread OK), but
>   `CollectErrorList` (`:178-213`) is a COM `ErrorItems` walk and MUST stay on the UI thread.
> - **m46 is a real bug:** the key is mapped TWICE per motion keystroke, and the insert
>   placement mapping has a drift (`TextMotionDispatcher.cs:168` Current vs
>   `PromptMotionRouter.cs:33` AfterCaret) — a behavior fix that changes the `a`/`A`/`I`
>   placement caret (flagged for the `neovisual-textinput-motions` gate).
> - **n3/n8/n18 are NOT safe to change:** n3 (per-test timeout is a documented accepted
>   "stop waiting", not a kill — `TestRunner.cs:57-68`), n8 (the only timeout-mechanism test;
>   shorten the sleep, don't delete), n18 (the only cross-implementation check). Each is
>   documented/kept, not "fixed".

## Goal

Fix ALL 106 code-review findings (incl. nits) from `docs/reviews/code-review.md`
(2026-10-06 refresh): the query-driven finder UI freeze + fzf subprocess storm
(M1/M2/M3), the mirrored geometric pipeline (M4), the text-input stale-frame
keyboard leak (M5), the two navigation/interop bugs (M6/M7), the `.cs`-only forest
(M8), the harness flakiness (M9/M10/M11), the overlay IsOpen race (M12), the
duplication/dead-code cluster (m24-m46), the test-infra nits (m51-m61, n2-n8,
n18), the harness gates (m62-m64, n9), and the doc drift (m65-m74, n1/n10-n12/n20)
— with every fix RED-proven at the unit level and the e2e scenarios QUEUED
(deferred to a capable machine).

## Approach (phases)

**Phase 0 — Query-driven finder core (M1, M2, M3, m2, m3, m4, m6, m7, m8, m9, m24, m25, m54).**
Make `GrepFinder.GetCandidatesAsync` truly async (M1); batch the Fzf per-file spawns into
ONE `fzf --filter` call with file-boundary mapping (M2); drop the eager `WarmContentCache`
(M3 — also deletes m24); move the file I/O out of the `FileContentCache` lock (m2); wrap
the literal-fallback scan off-thread (m3); run the CodeIssues TODO scan off-thread but keep
the COM Error List walk on the UI thread (m4); optimize `FzfLineMapper.Map` (m6); make
`GetLines`/`GetContent` share one cache entry (m7); suppress the spurious timeout log on a
cancelled gather (m8); give the grace `Task.Delay` a cancellation token (m9); merge
`GrepHit`/`FzfHit` (m25); add a direct `LiteralLineScanner.Scan` test (m54).

**Phase 1 — Navigation + interop (M6, M7, M11, m17, m19, m21, m22, m47, m48, n13, n14, n15).**
Per-window try/catch in `GetLinkedWindowsList` (M6); `c.Y > a.Bottom` for Down /
`c.Bottom < a.Y` for Up (M7 — **PLANNER CORRECTION:** this also REWRITES
`Run_WindowNavigationEngine_Down_OnePixelGapAccepted`, whose Down/Up assertions pin the
pre-M7 semantics); delete the vacuous `Down_ToleranceExcludes` test (M11);
`ThrowIfNotOnUIThread()` in the three `Dispose()` COM unhooks (m17); `lParam == IntPtr.Zero`
guard (m19); cancel the `FocusKeeper` DispatcherTimer tick on `Stop()` (m21); reclassify
`FindResults1`/`FindResults2` as non-text-input (m22); cache the `TryMove` visual-tree walk
(m47 — a controller-side cache across keys, invalidated on mode change; `SolutionExplorerController`
has no `WindowManager` reference); delete the `IsTextInputType` pass-through wrapper (m48);
avoid the throwaway `Dictionary` in `ResolveController` (n13); inline `ShouldLogFailure` (n14);
inline `GotoDispatcher.Decide` (n15).

**Phase 2 — Text-input stale-frame leak (M5).** Invalidate `_textInputSurfaceFocused` when
the main editor gains focus, keyed on the focused view's IDENTITY (the `ITextDocument.FilePath`
document-view discriminator VimModeTracker already uses — NOT `IsEditorFocused`, which the
Command Window's own `IWpfTextView` also sets), so a stale Command Window frame can never
claim keyboard ownership over a focused editor.

**Phase 3 — Geometric pipeline de-duplication (M4, n17).** Extract the pure geometric
selection engine (rect + direction + selection) into a dependency-free shared class in
Telescope, parameterized by **(allowNegativeGap, divide, strictEdge)** — the third parameter
is the PLANNER CORRECTION: the pane needs `strictEdge=false` (Down `c.Y > a.Y`, pinned by
`Run_FocusTarget_CtrlJFromListMovesToInput` — Input.Y=60 == List.Bottom=60) while the window
needs `strictEdge=true` (post-M7 Down `c.Y > a.Bottom`); `WindowNavigationEngine.SelectTarget`
and `FocusTargetModel.ResolveTarget` both delegate to it (HIGH-RISK — sequenced after M7;
the direction-table fallback is documented). n17 (the per-move `List<Candidate>` allocation)
is resolved by the shared engine.

**Phase 4 — Overlay + fzf/overlay nits (M12, m1, m5, m16, m20, m23, m39, m42, m43, m44, m45, m46, n16).**
Set `IsOpen=true` + `RequestShow()` BEFORE the `IsAvailableAsync` await + re-check after
(M12); guard `_previewNavigator.SetText` on file change (m1); log `results columns=`/
`results count=` on change only (m5); gate the hook callback on `HC_ACTION` (m16); marshal
`TelescopeController.Dispose` to the UI thread + close the overlay (m20); interlock
`PaneFailureTracker._emitted` (m23); use the centralized width constants (m39); delete
`_chooserMenu` (m42), `_fixedWidthSum` (m43), `IPane.IsFocusable` (m44), the dead
`OverlayKey.CtrlH`/`CtrlL` (m45); single `MapKey` + fix the insert-placement drift (m46);
guard the spurious `EnterInsert` focus log (n16).

**Phase 5 — Tool-window + forest (M8, m49, m50, m60).** Build the forest UNFILTERED + apply
the `.cs` filter only in `FirstSourceFilePath` (M8/m49); set `Expanded=true` before recursing
in `MapChildren` (M8); guard `_errorListGatherer` consistently (m50); add a
`KeybindingConfig.Merge` test (m60).

**Phase 6 — Vim + input nits (m10, m11, m12, m13, m14, m15, m18, m35, m36, m37, m38).**
Reorder `IsVisualStudioFocused()` after the `isKeyDown` check (m10); avoid the per-key
`DateTime.UtcNow` sentinel read (m11); fix the `_bufferToTextBuffer` leak (m12); guard the
`OnBufferClosed` reflection (m13); reject physical modifier keys as a leader (m14); clamp
the DTE `TextSelection` column (m15); fix the `KeyInjection` doc (m18); collapse the two
`ShouldRouteToolWindowKey` overloads (m35); use the pure shift gate (m36); remove the
write-only `LeaderResult.Action`/`SimpleShortcutResult.Action` (m37); resolve
`CurrentController` once (m38).

**Phase 7 — Duplication + dead code (m26, m27, m28, m29, m30, m31, m32, m33, m34, m40, m41, n19).**
Merge `DefinitionHit`/`ImplementationHit` (m26); merge `FinderColumns.Grep()`/`Fzf()` (m27);
merge `Files()`/`Recent()` (m28 — careful: the dir-cell getter differs, a real semantic diff);
one shared `HitCap` constant (m29); consolidate the 4-ctor test-seam sprawl (m30); delete
`VisibleIdsJoined` (m31); verify-then-delete the 2-arg `Compute` (m32); parameterize the
cross-slice `ErrorItems` iteration (m33); migrate `SanitizeSample` to `DiagnosticLog.SanitizeText`
(m34); delete `ColumnVisibilityModel.Ids`/`Catalog` (m40); delete the `PaneSelectionSync` shell
+ update its reflection test (m41); merge the near-identical `DefinitionFinder`/
`ImplementationFinder` bodies (n19).

**Phase 8 — Test infra (m51, m52, m53, m55, m56, m57, m58, m59, m61, n2, n3, n4, n5, n6, n7, n8, n18).**
Make `KillRegisteredBeforeWrite` deterministic (m51); restore the real flush timer after the
FlushTimer tests (m52); replace the reflection-coupled tests with seams (m53/m55/m57/m59/m61);
fix the duplicate FocusGuard assertions (m56); document the per-test-timeout thread-mutation
risk (m58); add the stack trace to failure output (n2); document the timeout "stop waiting"
(n3); delete `PositiveActionKeyCount` (n4); split the eager `Run_KeyNames_RoundTrip_WindowPrefix`
(n5); fix the stale Down-tolerance comments (n6); replace `Assert.True(x == y)` with
`Assert.Equal` (n7); shorten the `Run_TestRunner_Timeout` sleep (n8); keep the
cross-implementation check (n18 — documented).

**Phase 9 — Harness (M9, M10, m62, m63, m64, n9).** Fix the `telescope-goto` re-walk trigger
assert (M9); make `Run_FzfFilter_TimeoutRace` deterministic (M10); gate/drop the
`preview tokens=\d+` presence-only assertion (m62); make the four severity-nav assertions
deterministic (m63); make `explorer-open-navigation`'s `editor-view-opened` assertion
order-independent (m64); replace the fixed 1200ms sleeps in `iterate-telescope.ps1` with
polls (n9).

**Phase 10 — Docs + lint (m65, m66, m67, m68, m69, m70, m71, m72, m73, m74, n1, n10, n11, n12, n20).**
Add `e2e-queue.md` to the lint doc set + fix its 2 unresolved refs (m65); fix the
"Status: EMPTY" header (m66); annotate E2E-RC-1/2 + E2E-GOTO-1/2 DISCHARGED (m67); refresh
the progress.md header/baseline/run-order (m68/m69/m70); refresh code-review.md (m71 — incl.
the 77-vs-106 count discrepancy); fix the SKILL.md Down-tolerance + Panes/ parenthetical
(m72/m73); fix the agent slice-B file lists (m74); fix the WindowNavigator comment line ref
(n1); document the DOC-66-3 allowlist (n10); fix the progress.md dangling fragment + In-progress
(n11/n12); fix the `NeoVisualLog.Log` comment (n20).

## Acceptance criteria

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | The query-driven finders are hazard-free (M1/M2/M3/m2/m3/m4/m6/m7/m8/m9/m24/m25/m54): the Grep scan is truly async, the Fzf gather is ONE batched call, the warm-up is lazy, the cache I/O is out of the lock, the fallback is off-thread, the Error List walk stays UI-thread, the mapper is O(n), the cache shares entries, the spurious timeout log is gone | `grep hits=...` / `fzf hits=...` UNCHANGED (the fzf hit ORDER may change — M2 global ranking); the spurious `fzf filter failed: timeout` no longer fires (m8) | `Run_GrepFinder_GetCandidatesAsync_NoBlock`, `Run_FzfFinder_BatchedFilter`, `Run_FzfLineMapper_Boundary`, `Run_FileContentCache_IOOutsideLock`, `Run_CodeIssuesFinder_TodoScanOffThread`, `Run_LiteralLineScanner_Direct` |
| AC2 | The navigation/interop bugs are fixed (M6/M7/M11/m17/m19/m21/m22/m47/m48/n13/n14/n15): one stale frame can't kill navigation, Down targets the truly-below window, the vacuous test is gone, the COM unhooks assert the UI thread, the hook guards lParam, the keeper cancels its tick, FindResults are reclassified, TryMove walks once | `navigate direction=...` / `navigate activated index=...` UNCHANGED (the overlapping-window TARGET may change — M7); `toolwindow-move key=...` UNCHANGED | `Run_WindowFrameUtils_LinkedWindowIsolation`, `Run_WindowNavigationEngine_Down_BelowBottom`, `Run_FocusKeeper_TickCancelled`, `Run_ToolWindowTypeResolver_FindResultsNotTextInput`, `Run_SolutionExplorer_TryMoveSingleWalk` |
| AC3 | The text-input stale-frame leak is closed (M5): a stale Command Window frame can't claim keyboard ownership over a focused editor | `text-motion key=...` no longer fires for the main editor from a stale frame; `vim-mode=...` UNCHANGED | `Run_FocusGuard_TextInputSurfaceInvalidatedOnEditorFocus` |
| AC4 | The geometric pipeline is de-duplicated (M4/n17): one shared engine, both surfaces delegate, the pinned tie-break + no-op edges byte-identical | `focus target=Input|List|Preview` + `focus no-op:` UNCHANGED | the ~30 `Run_FocusTarget_*` + ~12 `Run_WindowNavigationEngine_*` tests stay GREEN + `Run_SharedGeometricEngine_Parameters` |
| AC5 | The overlay/fzf nits are cleaned (M12/m1/m5/m16/m20/m23/m39/m42/m43/m44/m45/m46/n16): the IsOpen race is closed, SetText is guarded, the results log fires on change, the hook gates HC_ACTION, Dispose marshals, the tracker interlocks, the dead code is gone, the placement drift is fixed | `results columns=`/`results count=` fire on change only (m5); `textinput-enter-input start\|end\|after caret=...` reflects the fixed placement (m46); `focus target=Input` no longer spurious on EnterInsert (n16) | `Run_TelescopeOverlay_IsOpenRace`, `Run_PreviewNavigator_SetTextGuarded`, `Run_ResultsLog_OnChange`, `Run_GlobalKeyboardHook_HcActionGate`, `Run_PaneFailureTracker_Interlocked`, `Run_PromptMotionRouter_SingleMapKey` |
| AC6 | The forest + tool-window fixes land (M8/m49/m50/m60): the search-box query resolves non-`.cs` items, the `.cs` filter is single, the gatherer guard is consistent, the Merge is tested | `solution-explorer select file=...` UNCHANGED | `Run_HierarchyForestBuilder_Unfiltered`, `Run_HierarchyResolver_SingleCsFilter`, `Run_KeybindingConfig_Merge` |
| AC7 | The Vim/input nits are cleaned (m10-m15/m18/m35-m38): the hook reorders the focus check, the sentinel read is avoided, the buffer map doesn't leak, the reflection is guarded, physical modifiers are rejected, the column is clamped, the overloads collapse, the dead state is gone, the controller resolves once | `vim-mode=...` / `leader-binding executed: ...` UNCHANGED | `Run_GlobalKeyboardHook_IsKeyDownFirst`, `Run_VimBufferSubscriptions_NoLeak`, `Run_KeybindingConfig_RejectsPhysicalModifierLeader`, `Run_RoslynGatherers_ColumnClamped`, `Run_InputHandler_SingleControllerResolution` |
| AC8 | The duplication/dead-code cluster is cleaned (m26-m34/m40/m41/n19) | all existing `[Telescope]`/`[NeoVisual]` lines UNCHANGED | `Run_FinderColumns_GrepFzfShared`, `Run_FinderColumns_FilesRecentShared`, `Run_HitCap_SharedConstant`, `Run_ResultColumn_DeadMembersRemoved`, `Run_PaneSelectionSync_ShellRemoved` |
| AC9 | The test infra is cleaned (m51-m61/n2-n8/n18) | n/a | `Run_FzfFilter_KillRegisteredBeforeWrite_Deterministic`, `Run_LogFileWriter_FlushTimerRestored`, `Run_Assert_StackTrace`, `Run_Assert_Equal`, `Run_TestRunner_TimeoutShort` |
| AC10 | The harness gates are hardened (M9/M10/m62/m63/m64/n9) | `diagnostic-nav direction=... severity=... target=...` asserted deterministically (m63); `preview tokens=` gated (m62) | e2e (deferred — E2E-CR77-1..6) + the harness code changes |
| AC11 | The docs + lint are refreshed (m65-m74/n1/n10-n12/n20) | n/a | doc-ref + doc-content lints PASS |

## Unit test plan

- **`tests/Telescope.Tests`** (the hermetic seams): `Run_GrepFinder_GetCandidatesAsync_NoBlock`
  (the override awaits `Task.Run` — no `.GetAwaiter().GetResult()` on the UI thread; the
  `_queryGeneration` guard discards stale results); `Run_FzfFinder_BatchedFilter` (ONE
  `FilterAsync` call per query, not N; the boundary-aware mapper returns (file, line));
  `Run_FzfLineMapper_Boundary` (the batched output maps back to the right file:line);
  `Run_FileContentCache_IOOutsideLock` (the file I/O runs outside the `_gate` lock);
  `Run_CodeIssuesFinder_TodoScanOffThread` (the TODO scan is off-thread; the Error List walk
  stays UI-thread); `Run_LiteralLineScanner_Direct` (null/empty/cap guards directly);
  `Run_FileContentCache_SharedEntry` (GetLines/GetContent share one entry); `Run_FzfFilter_NoSpuriousTimeoutOnCancel`
  (a cancelled gather does not log the timeout line); `Run_FzfFilter_GraceDelayToken`
  (the grace delay carries the cancellation token); `Run_TelescopeOverlay_IsOpenRace`
  (a close during the `IsAvailableAsync` await leaves `IsOpen` false); `Run_PreviewNavigator_SetTextGuarded`
  (SetText is skipped when the file is unchanged); `Run_ResultsLog_OnChange` (the results
  log fires on change only); `Run_PaneFailureTracker_Interlocked` (concurrent loggers emit
  once); `Run_PromptMotionRouter_SingleMapKey` (one MapKey per motion keystroke + the
  placement drift fixed); `Run_FinderColumns_GrepFzfShared` / `Run_FinderColumns_FilesRecentShared`
  (the merged catalogs); `Run_HitCap_SharedConstant`; `Run_ResultColumn_DeadMembersRemoved`;
  `Run_PaneSelectionSync_ShellRemoved`; `Run_FzfFilter_KillRegisteredBeforeWrite_Deterministic`;
  `Run_LogFileWriter_FlushTimerRestored`; `Run_SharedGeometricEngine_Parameters` (the shared
  engine reproduces both the window and pane contracts with the parameters).
- **`tests/NeoVisual.Tests`** (the hermetic seams): `Run_WindowFrameUtils_LinkedWindowIsolation`
  (one stale frame is skipped, the rest survive); `Run_WindowNavigationEngine_Down_BelowBottom`
  (Down targets the truly-below window; the overlapping window is excluded);
  `Run_FocusKeeper_TickCancelled`; `Run_ToolWindowTypeResolver_FindResultsNotTextInput`;
  `Run_SolutionExplorer_TryMoveSingleWalk`; `Run_FocusGuard_TextInputSurfaceInvalidatedOnEditorFocus`
  (the stale text-input frame no longer claims keyboard ownership over the editor);
  `Run_GlobalKeyboardHook_HcActionGate`; `Run_GlobalKeyboardHook_IsKeyDownFirst`;
  `Run_VimBufferSubscriptions_NoLeak`; `Run_KeybindingConfig_RejectsPhysicalModifierLeader`;
  `Run_KeybindingConfig_Merge`; `Run_RoslynGatherers_ColumnClamped`;
  `Run_InputHandler_SingleControllerResolution`; `Run_InputHandler_ShouldRouteToolWindowKey_SingleOverload`;
  `Run_LeaderSequenceMatcher_NoDeadAction`; `Run_Assert_StackTrace`; `Run_Assert_Equal`;
  `Run_TestRunner_TimeoutShort`.
- **RED proof:** every new test fails WITHOUT the fix and passes WITH it (the
  verify-tests-fail-without-fix discipline). The M4 shared-engine RED is the existing
  ~30 `Run_FocusTarget_*` + ~12 `Run_WindowNavigationEngine_*` suites (a behavior change
  breaks the pinned tests). The M7 RED is `Run_WindowNavigationEngine_Down_BelowBottom`
  (today the overlapping window wins). The M5 RED is `Run_FocusGuard_TextInputSurfaceInvalidatedOnEditorFocus`
  (today the stale frame claims ownership).

## Diagnostics (M-M7)

- **No NEW diagnostic literals.** This plan is diagnostic-neutral except these
  diagnostic-BEHAVIOR changes (each flagged with its harness site):
- **M2 (fzf hit ORDER):** the batched `fzf --filter` ranks globally, so the hit order
  changes. The `fzf hits=...` count is unchanged; the `opened fzf: file=... line=...`
  open-hit may differ. Harness site: `telescope-fzf` (E2E-CR77-1 — the open-hit assertion
  may need updating at the e2e gate).
- **M5 (leak fix):** `text-motion key=...` no longer fires for the main editor from a stale
  text-input frame. Harness site: `neovisual-textinput-motions` + `neovisual-editor-insert`
  (E2E-CR77-3).
- **M7 (nav target):** `navigate direction=...` unchanged; the overlapping-window TARGET may
  change. Harness site: `neovisual-window-nav` (E2E-CR77-2).
- **m5 (log frequency):** `results columns=`/`results count=... boxText=` fire on change
  only, not every render. Harness sites: `telescope-results-columns` + the results-count
  assertions (E2E-CR77-5).
- **m8 (spurious timeout):** the spurious `fzf filter failed: timeout after {ms}ms` no longer
  fires on a cancelled gather. No harness dependency (a failure path).
- **m46 (placement caret):** `textinput-enter-input start|end|after caret=...` reflects the
  fixed `a`/`A`/`I` placement (AfterCaret vs Current). Harness site: `neovisual-textinput-motions`
  (E2E-CR77-3).
- All other findings are diagnostic-neutral (the `[Telescope]`/`[NeoVisual]` literals
  byte-stable).

## Known-RED allowlist

- **NONE.** The baseline is all-GREEN (44/44 e2e, Telescope 297, NeoVisual 212 — the review
  verified both suites). Per-scenario flaky counts start at 0.

## E2E queue reference (e2e DEFERRED — queued in `e2e-queue.md`, status QUEUED)

> The user's 2026-10-06 instruction: "we are not on e2e capable machine so defer those to
> queue for later." The six gates below are QUEUED at handoff (Step 7 — appended to
> `e2e-queue.md` on user approval, status QUEUED; never created/executed now); they become
> READY when this plan is GREEN and are created/executed on a capable machine.

- **E2E-CR77-1** — `telescope-grep` + `telescope-fzf` stay GREEN (M1/M2/M3/m2/m3/m4 — the
  async scan + the batched fzf + the lazy warm must not change the finder behavior; the
  `fzf hits=`/`grep hits=` contract; the fzf hit ORDER may change per M2's global ranking —
  the open-hit assertion may need updating).
- **E2E-CR77-2** — `neovisual-window-nav` stays GREEN (M6/M7 — the per-window try/catch +
  the Down direction fix; `navigate direction=`/`navigate activated index=` unchanged; the
  overlapping-window target may change).
- **E2E-CR77-3** — `neovisual-textinput-motions` + `neovisual-editor-insert` (M5 — a stale
  text-input frame in normal mode while the main editor holds focus does NOT move the
  editor's caret; m46 — the placement caret fix).
- **E2E-CR77-4** — `telescope-focus-panes` stays GREEN (M4 — the shared geometric engine
  must not change the focus behavior — the pinned tie-break + no-op edges byte-identical).
- **E2E-CR77-5** — `telescope-preview` + `telescope-results-columns` (m1/m5 — the SetText
  guard + the results-log-on-change; the `results columns=`/`results count=`/`preview caret=`
  contract).
- **E2E-CR77-6** — the full 44-scenario fresh-boot suite re-run (the regression gate; covers
  M9/M10/m62/m63/m64/n9 + everything).

## Files to be touched

- **Telescope/:** `Finders/GrepFinder.cs`, `Finders/FzfFinder.cs`, `Finders/CodeIssuesFinder.cs`,
  `Finders/Utils/FileContentCache.cs`, `Finders/Utils/FzfLineMapper.cs`, `Finders/Utils/GrepHit.cs`,
  `Finders/Utils/FzfHit.cs`, `Finders/Utils/DefinitionHit.cs`, `Finders/Utils/ImplementationHit.cs`,
  `Finders/Utils/LiteralLineScanner.cs`, `Finders/Utils/ErrorItemsWalker.cs` (NEW — the shared
  `ErrorItems` walk, m33), `Finders/DefinitionFinder.cs`, `Finders/ImplementationFinder.cs`,
  `Finders/RecentFilesFinder.cs`, `Finders/FileFinder.cs`, `Filter/FzfFilter.cs`,
  `Overlay/TelescopeOverlay.cs`, `Overlay/Utils/FocusTargetModel.cs`, `Overlay/Utils/FinderColumns.cs`,
  `Overlay/Utils/ResultColumn.cs`, `Overlay/Utils/ResultRowCells.cs`, `Overlay/Utils/ResultsFormatter.cs`,
  `Overlay/Utils/ColumnWidths.cs`, `Overlay/Utils/OverlayKeyHandler.cs`, `Overlay/Utils/PromptMotionRouter.cs`,
  `Overlay/Utils/TextMotionDispatcher.cs`, `Overlay/Utils/GeometricSelectionEngine.cs` (NEW — the shared
  geometric engine, M4), `Overlay/Utils/ResultsLogGate.cs` (NEW — the results-log-on-change gate, m5),
  `Overlay/Utils/Panes/IPane.cs`, `Overlay/Utils/Panes/PaneSelectionSync.cs`,
  `Controller/TelescopeController.cs`, `Logging/Utils/PaneFailureTracker.cs`, `Logging/NeoVisualLog.cs`.
- **MyExtension/:** `Hooks/GlobalKeyboardHook.cs`, `Hooks/Utils/KeyInjection.cs`,
  `Input/InputHandler.cs`, `Input/Utils/KeybindingConfig.cs`, `Input/Utils/LeaderSequenceMatcher.cs`,
  `Input/Utils/SimpleShortcutMatcher.cs`, `Navigation/WindowNavigator.cs`,
  `Navigation/WindowNavigationEngine.cs`, `Navigation/Utils/WindowFrameUtils.cs`,
  `Navigation/Utils/WindowRect.cs`, `ToolWindows/WindowManager.cs`, `ToolWindows/GeneralToolWindowController.cs`,
  `ToolWindows/SolutionExplorerController.cs`, `ToolWindows/Utils/FocusGuard.cs`,
  `ToolWindows/Utils/FocusKeeper.cs`, `ToolWindows/Utils/ToolWindowTypeResolver.cs`,
  `ToolWindows/Utils/WindowTypeProbe.cs`, `ToolWindows/Utils/HierarchyForestBuilder.cs`,
  `ToolWindows/Utils/HierarchyResolver.cs`, `ToolWindows/Utils/TextMotionHelper.cs`,
  `Vim/Utils/VimBufferSubscriptions.cs`, `Vim/Utils/VimModeSource.cs`, `Package/MyExtensionPackage.cs`,
  `Package/Utils/ErrorListGatherer.cs`, `Package/Utils/RecentFilesGatherer.cs`,
  `Package/RoslynGatherers.cs`, `Package/Utils/TelescopeController.cs` (if the Dispose fix
  lands there), `Controller/GotoDispatcher.cs`.
- **tests/:** `tests/Telescope.Tests/Program.cs`, `tests/NeoVisual.Tests/Program.cs`,
  `tests/TestRunner.cs`.
- **tools/:** `tools/harness/test-e2e.ps1`, `tools/harness/iterate-telescope.ps1`,
  `tools/lint/check-doc-refs.ps1`, `tools/lint/check-doc-content.ps1`.
- **docs/:** `AGENTS.md`, `docs/spec.md`, `docs/progress.md`, `docs/e2e-queue.md`,
  `docs/reviews/code-review.md`, `.opencode/skills/vs-extension-dev/SKILL.md`,
  `.opencode/agent/neovim_review_hub.md`, `.opencode/agent/code-review-hub.md`.

## Open risks

1. **M4 (shared geometric engine) is the highest-risk change.** The pinned tie-break
   (Ctrl+K Input→Preview) + the no-op edges + the window-nav Down semantics must survive
   byte-identically. The ~30 `Run_FocusTarget_*` + ~12 `Run_WindowNavigationEngine_*` tests
   + `telescope-focus-panes`/`neovisual-window-nav` are the guard. Sequence AFTER M7 (the
   shared engine has the converged Down semantics). The engine is parameterized by
   **(allowNegativeGap, divide, strictEdge)** — the pane needs `strictEdge=false` (Down
   `c.Y > a.Y`, pinned by `Run_FocusTarget_CtrlJFromListMovesToInput`), the window needs
   `strictEdge=true` (post-M7 Down `c.Y > a.Bottom`). FALLBACK: the direction→target table
   (documented — the prior plan's table attempt failed on test churn, so the fallback
   requires rewriting the pane tests).
2. **M2's fzf batching changes the hit ORDER.** Global ranking is the intended behavior,
   but the `telescope-fzf` open-hit assertion may need updating at the e2e gate. The unit
   tests pin the new (file, line) mapping.
3. **M5's invalidation signal.** Gating on `IsEditorFocused` alone is WRONG (the Command
   Window's own IWpfTextView sets it). The invalidation must be keyed on the focused view's
   identity — the implementation-planner must pin the exact mechanism against the real code.
4. **M1's UI-thread affinity.** Only the pure `ScanFile` loop moves off-thread; the DTE
   enumeration + `EnsureSolutionCache` stay on the UI thread. The `_queryGeneration` guard
   is the marshal-back.
5. **m4's Error List COM walk must stay on the UI thread.** Only the pure TODO scan moves
   off-thread.
6. **m46's placement drift is a behavior fix.** The `a`/`A`/`I` placement caret changes —
   the `neovisual-textinput-motions` gate verifies.
7. **m5's log-frequency change.** `results columns=`/`results count=` fire on change only —
   the harness assertions must still be satisfiable (they assert after actions that change
   the results/selection).
8. **The review's count discrepancy.** The summary says 77 but the table has 106 rows. This
   plan covers ALL 106; the m71 doc refresh fixes the summary.
9. **The queue position.** The 106-findings code-review-fixes plan becomes the FIRST pending
   item at handoff (before Gap 5) — the counts are re-reads at the gate.

---

# Build Plan (106 findings — unit-only; e2e DEFERRED to E2E-CR77-1..6, QUEUED)

> **Lane:** unit-only. RED is proven at the unit level (the new tests fail WITHOUT the fix,
> pass WITH it — verify-tests-fail-without-fix). Verify-with = unit test NAMES + diagnostic
> FORMATS only. The queued e2e gates (E2E-CR77-1..6) are noted as SECONDARY checks on the
> harness/doc steps; they are never the primary Verify-with.
> **Known-RED allowlist: NONE** (baseline all-GREEN: Telescope 297, NeoVisual 212, 44/44 e2e).
> **Diagnostics:** this plan is diagnostic-NEUTRAL except the flagged diagnostic-BEHAVIOR
> changes (M2 fzf hit ORDER, M5 leak fix, M7 nav TARGET, m5 log frequency, m8 spurious
> timeout, m46 placement caret). No NEW diagnostic literals.
> **Build Plan authored by 4 parallel implementation-planners (2026-10-06):** Section A
> (Phases 0-1, BP-1..BP-23), Section B (Phases 2-4, BP-1..BP-17), Section C (Phases 5-7,
> BP-1..BP-28), Section D (Phases 8-10, BP-D1..BP-D37). 105 BP steps covering all 106
> findings (m58+n3 share one doc step; n6 coordinates with the Phase-1 M11 deletion).
>
> **Planner corrections baked into the Build Plan (the build-agent must not second-guess):**
> - **M4 (Section B):** the shared engine is parameterized by **(allowNegativeGap, divide,
>   strictEdge)** — the pane needs `strictEdge=false` (Down `c.Y > a.Y`, pinned by
>   `Run_FocusTarget_CtrlJFromListMovesToInput`), the window needs `strictEdge=true` (post-M7
>   Down `c.Y > a.Bottom`). The engine lives in Telescope (`MyExtension.csproj:32` references
>   `Telescope.csproj` — cycle-free); `WindowRect` + `PaneRect` implement the Telescope-defined
>   `IGeometricRect`.
> - **M7 (Section A):** the fix REWRITES `Run_WindowNavigationEngine_Down_OnePixelGapAccepted`
>   (its Down/Up assertions pin the pre-M7 semantics) — the plan's M11 claim that the behavior
>   is "pinned by" that test is wrong once M7 lands.
> - **m14 (Section C):** the two existing tests `Run_Keybinding_CustomLeaderParsed` +
>   `Run_KeybindingConfig_ParseLeaderRejectsModifiers` PIN the old `ControlKey`-honored
>   behavior and MUST be updated in the same step.
> - **m28 (Section C):** the `Files()`/`Recent()` merge must PRESERVE the dir-cell semantic
>   diff (`DirCell(p, projectRoot)` root-tail trim vs `Path.GetDirectoryName` full dir).
> - **m34 (Section C):** `DiagnosticLog.SanitizeText` is `internal` — the cross-project call
>   from `TextMotionHelper` needs it `public` or an `InternalsVisibleTo` extension.
> - **m46 (Section B):** `Run_TextMotionDispatcher_InsertPlacements:1873` pins the WRONG
>   `Current` for InsertAfter and must be updated to `AfterCaret`.
> - **M9 (Section D):** the review's "fresh `Get-LogCacheIndex` snapshot" fix is wrong as
>   literally stated (`Get-LogCacheIndex` returns the cache END, past the 0-gather line) — the
>   correct fix is the direct LogCache window scan.
> - **m71 (Section D):** the "34 findings open + 288/200" half is already fixed by the
>   2026-10-06 refresh; the remaining fix is the summary's 77-vs-106 count discrepancy.
> - **m47 (Section A):** `SolutionExplorerController` has no `WindowManager` reference — the
>   TryMove walk cache is controller-side, invalidated on mode change.
> - **m25 (Section A):** the GrepHit/FzfHit merge uses `using FzfHit = GrepHit` aliases (a true
>   merge, zero behavior change); m26 (Section C) uses the same alias pattern for
>   DefinitionHit/ImplementationHit.



---


# Section A — Build Plan: Phase 0 (Query-driven finder core) + Phase 1 (Navigation + interop)

> **Source:** `docs/reviews/code-review.md` (2026-10-06 refresh) + the plan's research
> corrections (baked in below). **Lane: feature (unit-only, e2e DEFERRED to `e2e-queue.md`).**
> RED is proven at the unit level only (verify-tests-fail-without-fix). No NEW diagnostic
> literals; two diagnostic-BEHAVIOR changes are flagged (M2 fzf hit ORDER, M7 nav TARGET).
> **Known-RED allowlist: NONE** (baseline all-GREEN: Telescope 297, NeoVisual 212, 44/44 e2e).
>
> **Coverage:** all 25 findings of Section A — Phase 0 (M1, M2, M3, m2, m3, m4, m6, m7, m8,
> m9, m24, m25, m54) and Phase 1 (M6, M7, M11, m17, m19, m21, m22, m47, m48, n13, n14, n15).
> 23 BP steps, each with Files / Change / Verify-with / Fails-if. Execution is one
> top-to-bottom pass by the build-agent; the phase headers are the hub's mid-plan checkpoints.

## Key source evidence (verified against the current tree)

- **M1 seam** — `Telescope/Finders/GrepFinder.cs:125-135`: the scan runs on
  `Task.Run(...).GetAwaiter().GetResult()` — the caller blocks on the UI thread. The chain:
  `TelescopeOverlay.cs:511` (`await finder.GetCandidatesAsync(query, token)`) →
  `FinderBase.cs:47-48` (the base default `GetCandidatesAsync` = `Task.FromResult(GetCandidates(query))`
  — a synchronous call wrapped in a completed task). GrepFinder does NOT override
  `GetCandidatesAsync`, so the base default runs the sync `GetCandidates` on the UI thread and
  blocks on `GetResult` for the whole scan. The `_queryGeneration` guard at
  `TelescopeOverlay.cs:507,514` already discards stale results.
- **M2 seam** — `Telescope/Finders/FzfFinder.cs:125-151`: `await _fzf.FilterAsync(lines, query,
  cancellationToken)` sits INSIDE the `foreach (string path in files)` loop — one fzf subprocess
  per file per keystroke. `IFzfEngine.FilterAsync` (`Telescope/Filter/IFzfEngine.cs:16`) takes a
  flat `IEnumerable<string>` candidate list, so batching ALL files' lines into ONE call is a
  finder-side change (the engine is untouched). fzf ranks across all stdin candidates
  (feature-researcher verified) → the hit ORDER changes (diagnostic-behavior change).
- **M7 seam** — `MyExtension/Navigation/WindowNavigationEngine.cs:58`: `IsInDirection` Down =
  `c.Y > a.Y` (strictly below the active's TOP, not bottom). `WindowRect.GapTo` Down
  (`WindowRect.cs:50`) = `Y - other.Bottom` is already correct (gap >= 0 when `c.Y > a.Bottom`);
  the bug is the direction filter admitting a partially-overlapping window (`a.Y < c.Y < a.Bottom`)
  whose negative gap becomes `minGap` and excludes the truly-below window. Fix is in
  `IsInDirection` only: Down `c.Y > a.Bottom`, Up `c.Bottom < a.Y`.

---

## Phase 0 — Query-driven finder core (M1, M2, M3, m2, m3, m4, m6, m7, m8, m9, m24, m25, m54)

### BP-1 — M1: GrepFinder.GetCandidatesAsync truly async (no UI-thread block)

- **Files:** `Telescope/Finders/GrepFinder.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** Add `public override async Task<IReadOnlyList<FinderEntry>> GetCandidatesAsync(string query = "", CancellationToken cancellationToken = default)` to `GrepFinder`. Empty query → return empty (the `WarmContentCache` call is dropped here — BP-3). Hermetic test path (`_testEnumerate != null`) → scan synchronously but honor the token in the loop. Production path → keep `ThreadHelper.ThrowIfNotOnUIThread()` + the DTE enumeration + `ProjectFileCache.EnsureSolutionCache` + `_fileCache.Get(...)` on the UI thread, then `await Task.Run(() => { foreach (path in files) { if (token.IsCancellationRequested) break; ScanFile(...); if (hits.Count >= HitCap) break; } }, cancellationToken)` — NO `.GetAwaiter().GetResult()`. Keep the sync `GetCandidates(query)` as `GetCandidatesAsync(query).GetAwaiter().GetResult()` (test-only + empty-query path; never called on the UI thread in production — the overlay uses the async override). `grep hits={hits.Count}` log unchanged.
- **Verify-with:** NEW `Run_GrepFinder_GetCandidatesAsync_NoBlock` (Telescope.Tests): a hermetic finder over a file with a needle; `GetCandidatesAsync("NEEDLE", preCancelledToken)` returns EMPTY (the override honors the token — RED today: the base default ignores the token and returns the hits); `GetCandidatesAsync("NEEDLE")` returns the hits. Existing `Run_GrepFinder_OffThreadScanSameHits`, `Run_GrepFinder_LineScanMatchesCaseInsensitive`, `Run_GrepFinder_QueryDrivenBehavior`, `Run_GrepFinder_ScanFileBackground` stay GREEN. Diagnostic: `grep hits={count}` byte-stable.
- **Fails-if:** `Run_GrepFinder_GetCandidatesAsync_NoBlock` returns hits for the pre-cancelled token (the override is missing / the base default still runs the sync path); the sync `GetCandidates` still contains the blocking `Task.Run(...).GetAwaiter().GetResult()` in the production path.

### BP-2 — M2: FzfFinder batches ALL files' lines into ONE fzf --filter call (boundary-aware mapping)

- **Files:** `Telescope/Finders/FzfFinder.cs`; `Telescope/Finders/Utils/FzfLineMapper.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** In `FzfFinder.GetCandidatesAsync`, replace the per-file `FilterAsync` loop with: (1) read every file's lines (off-thread — see BP-5) into a `List<(string Path, IReadOnlyList<string> Lines)>`; (2) build ONE batched candidate list via a new pure `FzfLineMapper.BuildCandidates(filesLines)` — each line becomes `"{fileIndex}\u0001{lineText}"` (the `\u0001` SOH control char is the file-boundary marker; untypeable in the query box); (3) ONE `await _fzf.FilterAsync(candidates, query, cancellationToken)`; (4) map the ranked output back via a new pure `FzfLineMapper.MapBatched(filesLines, matchedLines)` returning `(int FileIndex, int LineNumber)` pairs in fzf's GLOBAL rank order (per-file ordinal consumption of duplicate line texts via a `Dictionary<string, Queue<int>>` per file — O(n+m), no reordering). Cap at `HitCap` after mapping. `fzf hits={hits.Count}` log unchanged. **Diagnostic-behavior change:** global ranking changes the hit ORDER — the `opened fzf: file=... line=...` open-hit may differ (flagged for the deferred `telescope-fzf` gate, E2E-CR77-1).
- **Verify-with:** NEW `Run_FzfFinder_BatchedFilter` (Telescope.Tests): a hermetic finder over 2 files with a `FakeFzfEngine(true)`; `GetCandidatesAsync("NEEDLE")` → `Assert.Equal(1, fake.FilterCalls)` (ONE batched call, not 2) + the correct hits. NEW `Run_FzfLineMapper_Boundary` (Telescope.Tests): `BuildCandidates` over 2 files + `MapBatched` maps the matched output back to the right `(fileIndex, lineNumber)` pairs in global order. Existing `Run_FzfFinder_FuzzyMatchReportsHits`, `Run_FzfFinder_Cancellation` (the token still reaches the single `FilterAsync`), `Run_FzfFinder_CacheEnumeratesOnce`, `Run_FzfFinder_HitCapBounded` stay GREEN. Diagnostic: `fzf hits={count}` byte-stable.
- **Fails-if:** `Run_FzfFinder_BatchedFilter` sees `FilterCalls == 2` (the per-file loop survived); `Run_FzfLineMapper_Boundary` maps a hit to the wrong file/line (the marker parse or the ordinal consumption is wrong); a hit's `LineText` contains the marker (the `\u0001` split is wrong).

### BP-3 — M3 + m24: drop the eager WarmContentCache (deletes the duplicated WarmContentCache/WarmFile)

- **Files:** `Telescope/Finders/GrepFinder.cs`; `Telescope/Finders/FzfFinder.cs`.
- **Change:** Delete `WarmContentCache()` + `WarmFile()` from BOTH `GrepFinder` (GrepFinder.cs:152-194) and `FzfFinder` (FzfFinder.cs:211-253) — the byte-identical pair (m24). Remove the `WarmContentCache()` call from the empty-query path of both finders (GrepFinder.cs:86, FzfFinder.cs:95); the empty-query path just returns `Array.Empty<FinderEntry>()`. The first gather populates the shared `FileContentCache` lazily (the gather runs off-thread after BP-1/BP-2/BP-5). `EnsureSolutionCache` stays (it is the shared solution-invalidation helper, not the warm-up).
- **Verify-with:** compile-RED (the `WarmContentCache`/`WarmFile` members are gone → any residual reference fails to compile). Existing `Run_GrepFinder_EmptyQueryReturnsZeroCandidates`, `Run_GrepFinder_EmptyQueryCleanEmptyNoFailureLog`, `Run_FzfFinder_EmptyQueryReturnsZeroCandidates`, `Run_FzfFinder_EmptyQueryCleanEmptyNoFailureLog` stay GREEN (the empty-query path still returns empty with no failure log). Diagnostic: `grep hits=`/`fzf hits=` byte-stable.
- **Fails-if:** a residual `WarmContentCache`/`WarmFile` reference fails the build (m24 not fully deleted); the empty-query path logs a gather/failure line it must not; the first non-empty gather returns stale/empty hits (the lazy populate is broken).

### BP-4 — m2: FileContentCache file I/O outside the `_gate` lock

- **Files:** `Telescope/Finders/Utils/FileContentCache.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** In `GetLines` and `GetContent`, move the injected `_reader`/`_contentReader` calls OUT of the `lock (_gate)` (double-checked locking): read the timestamp + check the cache under the lock; on a miss, release the lock, run the reader, re-acquire and `Put`. The timestamp read may stay under the lock (cheap stat); the ~3.5ms cold `ReadAllLines`/`ReadAllText` must not serialize every other cache access. `Touch`/`Put`/`EvictIfNeeded` stay under the lock.
- **Verify-with:** NEW `Run_FileContentCache_IOOutsideLock` (Telescope.Tests): a reader that blocks on a `ManualResetEventSlim` for path "a"; `Task.Run(() => cache.GetLines("a"))` blocks in the reader; a second `Task.Run(() => cache.GetLines("b"))` must complete within 1s (RED today: the first holds the lock while blocked → the second blocks on the lock → `Wait(1000)` returns false). Existing `Run_FileContentCache_ThreadSafe`, `Run_FileContentCache_CachedRead`, `Run_FileContentCache_Lru` stay GREEN.
- **Fails-if:** `Run_FileContentCache_IOOutsideLock` times out on the second read (the reader still runs under the lock); a concurrent `GetLines`/`GetContent` throws or corrupts the dictionary (the double-checked unlock/re-lock is wrong).

### BP-5 — m3: FzfFinder literal-fallback scan off-thread

- **Files:** `Telescope/Finders/FzfFinder.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** In `GetCandidatesAsync`, run the per-file content reads AND the literal-fallback `LiteralLineScanner.Scan` loop inside `Task.Run(..., cancellationToken)` (the fallback path — fzf unavailable). The batched fzf path (BP-2) shares the same off-thread content-read block. The DTE enumeration (`EnumerateFiles`) stays on the UI thread. `fzf unavailable — literal fallback` log unchanged.
- **Verify-with:** NEW `Run_FzfFinder_LiteralFallbackOffThread` (Telescope.Tests): a `FileContentCache` whose injected reader records `Thread.CurrentThread.ManagedThreadId`; a `FakeFzfEngine(false)` (unavailable) finder; `GetCandidatesAsync("NEEDLE")` → the recorded thread differs from the test thread (RED today: the fallback + cold `GetLines` run on the calling thread). Existing `Run_FzfFinder_UnavailableFallsBackToLiteralScan` stays GREEN (2 hits, `FilterCalls == 0`, the fallback diagnostic).
- **Fails-if:** `Run_FzfFinder_LiteralFallbackOffThread` records the UI thread (the fallback still runs inline); the fallback returns the wrong hits (the off-thread scan lost the cap/order).

### BP-6 — m4: CodeIssuesFinder TODO scan off-thread; the COM Error List walk stays UI-thread

- **Files:** `Telescope/Finders/CodeIssuesFinder.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** In `GatherHits`, wrap the `CollectTodos` loop (both the hermetic `_testFileSource` path and the production `_fileCache.Get(...)` path) in `Task.Run(...).GetAwaiter().GetResult()` — the TODO scan is pure file I/O (off-thread OK). `CollectErrorList(dte, issues)` (CodeIssuesFinder.cs:178-213, the COM `ErrorItems` walk) MUST stay on the UI thread, OUTSIDE the `Task.Run` (it already asserts `ThreadHelper.ThrowIfNotOnUIThread()` at :180). `CollectTodos` mutates the shared `issues` list only from the background thread while the UI thread is blocked on `GetResult` — no concurrent access.
- **Verify-with:** NEW `Run_CodeIssuesFinder_TodoScanOffThread` (Telescope.Tests): a `FileContentCache` whose injected reader records the thread id; a hermetic `CodeIssuesFinder`; `GetCandidates("")` → the recorded thread differs from the test thread (RED today: the hermetic TODO scan runs inline). Existing CodeIssuesFinder tests stay GREEN. The Error-List-stays-UI-thread half is verified by code review (`CollectErrorList` outside the `Task.Run` + its existing UI-thread assert).
- **Fails-if:** `Run_CodeIssuesFinder_TodoScanOffThread` records the UI thread; `CollectErrorList` is moved inside the `Task.Run` (a COM walk off the UI thread — the `ThrowIfNotOnUIThread` at :180 would throw in production).

### BP-7 — m6: FzfLineMapper.Map O(n·m) → O(n+m)

- **Files:** `Telescope/Finders/Utils/FzfLineMapper.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** Rewrite `FzfLineMapper.Map(fileLines, matchedLines)` from the nested `for` scan (O(n·m), up to ~200k string comparisons per file at HitCap) to a single pass: build a `Dictionary<string, Queue<int>>` (ordinal) of line-text → unconsumed 1-based line numbers, then for each matched line pop the next unconsumed line (O(n+m)). Behavior byte-identical (ordinal consumption, unknown lines skipped). `MapBatched` (BP-2) already uses the same dictionary technique.
- **Verify-with:** Existing `Run_FzfLineMapper_ExactLineMapsToLineNumber`, `Run_FzfLineMapper_DuplicateLinesMapOrdinal`, `Run_FzfLineMapper_UnknownLineSkipped` stay GREEN (they pin the ordinal-consumption contract the rewrite must preserve). No diagnostic.
- **Fails-if:** any of the three `Run_FzfLineMapper_*` tests fail (the rewrite changed the ordinal-consumption or skip semantics); the dictionary keying is case-sensitive when it must be ordinal (it must stay `StringComparison.Ordinal`).

### BP-8 — m7: FileContentCache GetLines/GetContent share one entry

- **Files:** `Telescope/Finders/Utils/FileContentCache.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** When `GetLines` finds an entry with `Content != null` but `Lines == null`, derive the lines from the cached content (split on `\r\n`, `\n`, `\r` — matching `File.ReadAllLines`) and cache them WITHOUT a disk read. When `GetContent` finds an entry with `Lines != null` but `Content == null`, derive the content by joining with `"\n"` and cache it. `GetContent` has NO production callers (verified — test-only), so the lossy lines→content join is safe. The `CacheEntry` already stores both fields; the fix is the cross-derivation.
- **Verify-with:** NEW `Run_FileContentCache_SharedEntry` (Telescope.Tests): a cache with counting `reader`/`contentReader`; `GetContent("a")` then `GetLines("a")` → the reader count stays 1 (RED today: `GetLines` re-reads because the entry's `Lines == null`). Existing `Run_FileContentCache_ThreadSafe`, `Run_FileContentCache_Lru`, `Run_FileContentCache_Shared` stay GREEN.
- **Fails-if:** `Run_FileContentCache_SharedEntry` sees the reader count increment on the second access; the derived lines differ from `File.ReadAllLines` for `\r\n` content (the split is wrong); the LRU exact-total invariant breaks (the derivation must not double-insert).

### BP-9 — m8 + m9: FzfFilter suppresses the spurious timeout log on a cancelled gather; the grace delay gets the cancellation token

- **Files:** `Telescope/Filter/FzfFilter.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** In `FilterAsync` (FzfFilter.cs:265-278): (m9) create the grace delay with the caller's token — `Task.Delay(FilterTimeoutGraceMs, cancellationToken)`; (m8) after the grace `Task.WhenAny`, check `if (cancellationToken.IsCancellationRequested)` → cancel the dedicated `timeoutCts`, decrement `PendingTimeoutCount`, observe the pipe-read tasks (`AwaitedReadCount += 2` + the fault-only continuation), and return `lines` SILENTLY (no `fzf filter failed: timeout after {ms}ms` line) — mirroring the existing cancellation path at :235-246. The genuine-timeout path still logs the line.
- **Verify-with:** NEW `Run_FzfFilter_NoSpuriousTimeoutOnCancel` (Telescope.Tests): a 30s-hanging stub, `FilterTimeoutMs = 100`, cancel at ~150ms (inside the 250ms grace window) → the result is the unfiltered list AND the log contains NO `fzf filter failed: timeout after` (RED today: the line is logged). NEW `Run_FzfFilter_GraceDelayToken` (Telescope.Tests): same setup, cancel at ~150ms, assert the `FilterAsync` returns within ~150ms of the cancel (RED today: the uncancelled grace delay waits the remaining ~200ms). Existing `Run_FzfFilter_TimeoutAwaitsTasks`, `Run_FzfFilter_CancellationObservesTasks`, `Run_FzfFilter_TimeoutTimerCancelled` stay GREEN.
- **Fails-if:** `Run_FzfFilter_NoSpuriousTimeoutOnCancel` finds the timeout line in the log (the cancelled-gather path still logs); `Run_FzfFilter_GraceDelayToken` takes ~200ms+ to return after the cancel (the grace delay is not cancelled); `PendingTimeoutCount`/`AwaitedReadCount` drift on the new path (the counters must mirror the existing cancellation path).

### BP-10 — m25: merge the byte-identical GrepHit/FzfHit hit models

- **Files:** `Telescope/Finders/Utils/FzfHit.cs` (delete); `Telescope/Finders/FzfFinder.cs`; `Telescope/Overlay/Utils/FinderColumns.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** Keep `GrepHit` (`Telescope/Finders/Utils/GrepHit.cs`) as the canonical hit model. Delete `FzfHit.cs`. Add `using FzfHit = Telescope.Finders.GrepHit;` in `FzfFinder.cs` (the `FinderBase<FzfHit>`/`Action<FzfHit>`/`new FzfHit(...)` references resolve to `GrepHit`), `FinderColumns.cs` (the `Cell<FzfHit>` column cells), and `tests/Telescope.Tests/Program.cs` (the `FzfHit` payload casts). The two classes are byte-identical (`FileLocation` + `LineText`), so the alias is a true merge.
- **Verify-with:** compile-RED (FzfHit.cs deleted → every residual `FzfHit` reference must be covered by the alias or the build fails). Existing `Run_FzfFinder_PayloadRoundTripsFzfHit`, `Run_GrepFinder_PayloadRoundTripsGrepHit`, `Run_FzfFinder_OnSelectedOpensHitAtLine` stay GREEN (the alias keeps the payload casts valid). No diagnostic.
- **Fails-if:** a residual `FzfHit` reference fails the build (the alias is missing in a file); a payload round-trip test fails (the merged model lost a member).

### BP-11 — m54: direct unit test for LiteralLineScanner.Scan

- **Files:** `tests/Telescope.Tests/Program.cs`.
- **Change:** Test-only (no production change). Add a direct test for `LiteralLineScanner.Scan` (`Telescope/Finders/Utils/LiteralLineScanner.cs:14`) covering the null/empty/cap guards + the case-insensitive substring + the cap bound, which today are only covered indirectly through GrepFinder/FzfFinder.
- **Verify-with:** NEW `Run_LiteralLineScanner_Direct` (Telescope.Tests): `Scan(null!, "q", 10)` → empty; `Scan(lines, "", 10)` → empty; `Scan(lines, "q", 0)` → empty; case-insensitive substring returns the right 1-based lines; the cap bounds the result. The test passes against the current code (the guards already work) — this is a coverage addition, not a RED.
- **Fails-if:** any `Run_LiteralLineScanner_Direct` assertion fails (a guard or the cap is broken); the test is not registered in the runner.

**Phase 0 mid-point verify (hub checkpoint):** `dotnet run --project tests/Telescope.Tests` — the 11 new tests (`Run_GrepFinder_GetCandidatesAsync_NoBlock`, `Run_FzfFinder_BatchedFilter`, `Run_FzfLineMapper_Boundary`, `Run_FileContentCache_IOOutsideLock`, `Run_FzfFinder_LiteralFallbackOffThread`, `Run_CodeIssuesFinder_TodoScanOffThread`, `Run_FileContentCache_SharedEntry`, `Run_FzfFilter_NoSpuriousTimeoutOnCancel`, `Run_FzfFilter_GraceDelayToken`, `Run_LiteralLineScanner_Direct`) pass and the existing Telescope suite (297) stays GREEN. `grep hits=`/`fzf hits=` byte-stable; the fzf hit ORDER may change (M2).

---

## Phase 1 — Navigation + interop (M6, M7, M11, m17, m19, m21, m22, m47, m48, n13, n14, n15)

### BP-12 — M6: per-window try/catch in GetLinkedWindowsList (one stale COM frame must not kill navigation)

- **Files:** `MyExtension/Navigation/Utils/WindowFrameUtils.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** In `GetLinkedWindowsList` (WindowFrameUtils.cs:57-65), wrap each window's `window?.LinkedWindowFrame` read AND the `CompareWindows` call (which reads `Caption`/`Type`) in a per-window `try/catch` that skips the bad window (a stale/disconnected RCW must not propagate through `LinkedTo` → `BuildActiveWindows` → the ctor catch and degrade ALL navigation to a no-op). `CompareWindows` itself stays unchanged (its `ThrowIfNotOnUIThread` + null handling are correct).
- **Verify-with:** NEW `Run_WindowFrameUtils_LinkedWindowIsolation` (NeoVisual.Tests): set up `ThreadHelper.uiThreadDispatcher` (the established pattern); a parent `FakeWindow` (the existing test fake at NeoVisual.Tests:3875), a linked window whose `LinkedWindowFrame` returns the parent, and a stale window whose `LinkedWindowFrame` THROWS (a new test fake — the existing `FakeWindow` is sealed, so the test adds a throwing variant or unseals it); `GetLinkedWindowsList(parent, [linked, stale])` → the call does NOT throw and the result contains the linked window (RED today: the stale window's throw propagates). Diagnostic: `navigate direction=...`/`navigate activated index=...` byte-stable for healthy windows.
- **Fails-if:** `Run_WindowFrameUtils_LinkedWindowIsolation` throws (the per-window catch is missing); the linked window is dropped (the catch swallowed a healthy window); the `CompareWindows` Caption/Type reads still throw uncaught.

### BP-13 — M7: Down targets the truly-below window (c.Y > a.Bottom); Up mirrors (c.Bottom < a.Y)

- **Files:** `MyExtension/Navigation/WindowNavigationEngine.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** In `IsInDirection` (WindowNavigationEngine.cs:57-58): Down `c.Y > a.Y` → `c.Y > a.Bottom`; Up `c.Y < a.Y` → `c.Bottom < a.Y`. `WindowRect.GapTo` is already correct (Down = `Y - other.Bottom`, Up = `other.Y - Bottom`) — no change. This excludes partially-overlapping windows (negative gap) so the truly-below/above window wins. **Diagnostic-behavior change:** `navigate direction=...` unchanged; the overlapping-window TARGET may change (flagged for the deferred `neovisual-window-nav` gate, E2E-CR77-2). **This invalidates the existing `Run_WindowNavigationEngine_Down_OnePixelGapAccepted` (NeoVisual.Tests:2171) — its Down assertion (c.Y=101, an overlapping window, accepted) and its Up assertion (c.Y=99, Bottom=149, accepted) both pin the OLD semantics and MUST be rewritten in this step** to the new contract: Up accepts a candidate strictly above the active's top (`c.Bottom < a.Y`, e.g. c.Y=49/Bottom=99 for active Y=100), Down accepts a candidate strictly below the active's bottom (`c.Y > a.Bottom`, e.g. c.Y=201 for active Bottom=200).
- **Verify-with:** NEW `Run_WindowNavigationEngine_Down_BelowBottom` (NeoVisual.Tests): active `(100,100,100,100)` (Bottom=200); candidate A `(100,150,100,100)` (overlapping, gap -50, adjacency 100) + candidate B `(100,250,50,50)` (truly below, gap 50, adjacency 50); `SelectTarget(..., Down, settings)` → `Assert.Equal(1, ...)` (RED today: A wins on adjacency → returns 0). REWRITE `Run_WindowNavigationEngine_Down_OnePixelGapAccepted` to the new symmetric contract (above). Existing `Run_WindowNavigationEngine_Up_PicksLargestAdjacency`, `Run_WindowNavigationEngine_Left_PicksLargestAdjacency`, `Run_WindowNavigationEngine_Right_PicksLargestAdjacency`, `Run_WindowNavigationEngine_EmptyCandidates_ReturnsNull` stay GREEN.
- **Fails-if:** `Run_WindowNavigationEngine_Down_BelowBottom` returns 0 (the overlapping window still passes the direction filter); the rewritten `Run_WindowNavigationEngine_Down_OnePixelGapAccepted` fails (the new Up/Down edge semantics are wrong); an existing Up/Left/Right test regresses (the direction filter change leaked).

### BP-14 — M11: delete the vacuous Run_WindowNavigationEngine_Down_ToleranceExcludes

- **Files:** `tests/NeoVisual.Tests/Program.cs`.
- **Change:** Delete `Run_WindowNavigationEngine_Down_ToleranceExcludes` (NeoVisual.Tests:2159-2169). It passes vacuously — the `>1` tolerance was removed in 349fc05, so both candidates pass the direction filter and the assertion is satisfied by the last-wins tie-break; its name/comment falsely claim a tolerance filter exists. The actual contract is pinned by the rewritten `Run_WindowNavigationEngine_Down_OnePixelGapAccepted` (BP-13) + `Run_WindowNavigationEngine_Down_BelowBottom` (BP-13).
- **Verify-with:** compile-RED (the test method is deleted → the runner no longer lists it; the suite compiles and passes without it). The BP-13 tests pin the real contract.
- **Fails-if:** the deleted test is still registered (the runner fails or lists it); the suite count drops unexpectedly beyond the one deleted test.

### BP-15 — m17: ThrowIfNotOnUIThread() before the COM event unhook in the three Dispose() methods

- **Files:** `MyExtension/Package/Utils/RecentFilesGatherer.cs`; `MyExtension/Package/Utils/ErrorListGatherer.cs`; `MyExtension/ToolWindows/WindowManager.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** Add `ThreadHelper.ThrowIfNotOnUIThread()` as the FIRST line of `RecentFilesGatherer.Dispose()` (RecentFilesGatherer.cs:51, before the `_documentEvents.DocumentOpened -=` unhook), `ErrorListGatherer.Dispose()` (ErrorListGatherer.cs:160, before the `_buildEvents.OnBuildDone -=`/`_documentEvents.DocumentSaved -=` unhooks), and `WindowManager.Dispose()` (WindowManager.cs:399, before `_monitorSelection.UnadviseSelectionEvents`). The COM event unhooks are VS-API calls and must run on the UI thread.
- **Verify-with:** NEW `Run_RecentFilesGatherer_Dispose_RequiresUiThread` (NeoVisual.Tests): set up `ThreadHelper.uiThreadDispatcher`; construct a `RecentFilesGatherer`; call `Dispose()` from a background thread → it throws (RED today: no assert, the unhook runs off-thread). Existing `Run_RecentFilesGatherer_Dispose` stays GREEN (the UI-thread Dispose still unhooks). The ErrorListGatherer + WindowManager variants are the same one-line change (compile-RED + code review). No diagnostic.
- **Fails-if:** `Run_RecentFilesGatherer_Dispose_RequiresUiThread` does not throw on the background thread (the assert is missing); `Run_RecentFilesGatherer_Dispose` breaks (the assert fires on the UI thread).

### BP-16 — m19: lParam == IntPtr.Zero guard before Marshal.ReadInt32

- **Files:** `MyExtension/Hooks/GlobalKeyboardHook.cs`.
- **Change:** In `HookCallback` (GlobalKeyboardHook.cs:100), before `int vkCode = Marshal.ReadInt32(lParam);`, add `if (lParam == IntPtr.Zero) return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);` — an exception in the hook callback is fatal (Windows silently removes the hook). Place the guard after the `IsVisualStudioFocused()` check (the current position) so the common path is unchanged.
- **Verify-with:** compile-RED (the guard compiles; the hook path is unchanged for non-zero `lParam`). No hermetic test exists for the private callback (it needs a real hook) — verified by code review + the existing hook tests staying GREEN. No diagnostic.
- **Fails-if:** `Marshal.ReadInt32(lParam)` is still reachable with `lParam == IntPtr.Zero` (the guard is missing or placed after the read); the guard is placed before the `IsVisualStudioFocused()` check (a behavior change to the hot path).

### BP-17 — m21: FocusKeeper cancels a superseded keeper's queued tick

- **Files:** `MyExtension/ToolWindows/Utils/FocusKeeper.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** In `FocusKeeper.Run` (FocusKeeper.cs:23-54), the tick handler currently calls `tick(...)` FIRST and only checks `ReferenceEquals(_current, keeper)` when stopping — so a tick queued in the dispatcher before `_current?.Stop()` (the supersede at :25) still calls `tick(...)` and re-asserts the old target once. Add `if (!ReferenceEquals(_current, keeper)) return;` as the FIRST line of the tick handler — a superseded keeper's queued tick is a no-op. (The `_current` field is set to the new keeper before the old one's queued tick fires.)
- **Verify-with:** NEW `Run_FocusKeeper_TickCancelled` (NeoVisual.Tests): run keeper 1 (1-5ms interval, tick increments a counter), immediately run keeper 2 (supersedes), pump the dispatcher (`Dispatcher.PushFrame` loop for ~30ms) so any queued tick fires, assert keeper 1's counter is 0. NOTE: the RED is the guard mechanism — the race (a tick queued before `Stop()`) is timing-dependent, so if the test passes without the fix the guard is still the correct fix (verified by code review); the test pins the contract. Existing `Run_FocusKeeper_ResetAfterDispose` stays GREEN. No diagnostic.
- **Fails-if:** a superseded keeper's tick calls `tick(...)` (the guard is missing or placed after the `tick` call); `Run_FocusKeeper_ResetAfterDispose` breaks (the guard interferes with the normal stop path).

### BP-18 — m22: reclassify FindResults1/FindResults2 as non-text-input

- **Files:** `MyExtension/ToolWindows/Utils/ToolWindowTypeResolver.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** Remove `case ToolWindowType.FindResults1:` and `case ToolWindowType.FindResults2:` from `ToolWindowTypeResolver.IsTextInputType` (ToolWindowTypeResolver.cs:168-169) — the Find Results windows are read-only lists, not text-input surfaces; they must not start in input mode or inherit the `OwnsKeyboard` exemption.
- **Verify-with:** NEW `Run_ToolWindowTypeResolver_FindResultsNotTextInput` (NeoVisual.Tests): `Assert.False(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.FindResults1))` and `Assert.False(...FindResults2)` (RED today: both return true). Existing `Run_ToolWindowMode_TextInputTypesClassified`/`Run_ToolWindowMode_NavigationTypesClassified` stay GREEN (they don't assert FindResults). No diagnostic.
- **Fails-if:** `Run_ToolWindowTypeResolver_FindResultsNotTextInput` fails (the cases are still in the switch); a FindResults window still starts in input mode (the classification is read elsewhere — verify no second switch).

### BP-19 — m47: cache the SolutionExplorerController TryMove visual-tree walk

- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** `TryMove` (SolutionExplorerController.cs:203-221) resolves the focused WPF text box via `_findFocusedTextBox()` (a visual-tree walk) per routed key, bypassing the M1/C3 focus-change caching. Cache the resolved box in a field (e.g. `_cachedFocusedBox`), resolved once and reused across consecutive `TryMove` calls, invalidated on mode change (`EnterInputMode`/`ExitInputMode` — the `_pendingStyleBox` pattern at :40 already caches across `ExitInputMode`). The `_findFocusedTextBox` seam stays (the existing once-per-key contract is preserved).
- **Verify-with:** Existing `Run_SolutionExplorer_TryMoveSingleWalk` (NeoVisual.Tests:3434) stays GREEN (the seam is still called at most once per key when a box is focused). NEW `Run_SolutionExplorer_TryMoveCachedAcrossKeys` (NeoVisual.Tests): the counting seam; two consecutive `TryMove(Keys.X)` calls → the seam is called ONCE (RED today: the second call re-walks → count 2). No diagnostic.
- **Fails-if:** `Run_SolutionExplorer_TryMoveCachedAcrossKeys` sees the seam called twice (the cache is missing); `Run_SolutionExplorer_TryMoveSingleWalk` breaks (the cache changed the once-per-key contract); a stale box is used after a mode change (the invalidation is missing).

### BP-20 — m48: delete the GeneralToolWindowController.IsTextInputType pass-through wrapper

- **Files:** `MyExtension/ToolWindows/GeneralToolWindowController.cs`; `MyExtension/ToolWindows/WindowManager.cs`; `MyExtension/ToolWindows/ToolWindowControllerBase.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** Delete `GeneralToolWindowController.IsTextInputType` (GeneralToolWindowController.cs:84-87 — a pure pass-through of `ToolWindowTypeResolver.IsTextInputType`). Replace the call sites with `ToolWindowTypeResolver.IsTextInputType`: `WindowManager.cs:310,383,392` and `ToolWindowControllerBase.cs:27`. Update the test references `Run_ToolWindowMode_TextInputTypesClassified` (NeoVisual.Tests:469-472) + `Run_ToolWindowMode_NavigationTypesClassified` (:477-479) to call `ToolWindowTypeResolver.IsTextInputType` directly.
- **Verify-with:** compile-RED (the wrapper is deleted → any residual `GeneralToolWindowController.IsTextInputType` reference fails the build). The updated `Run_ToolWindowMode_TextInputTypesClassified`/`Run_ToolWindowMode_NavigationTypesClassified` + `Run_GeneralToolWindowController_InitialModeFromType` stay GREEN (the initial-mode-from-type behavior is unchanged). No diagnostic.
- **Fails-if:** a residual `GeneralToolWindowController.IsTextInputType` reference fails the build; a mode-classification test fails (a call site was replaced with the wrong resolver).

### BP-21 — n13: avoid the throwaway Dictionary in ResolveController

- **Files:** `MyExtension/ToolWindows/WindowManager.cs`.
- **Change:** `ResolveController` (WindowManager.cs:263-270) allocates `new Dictionary<ToolWindowType, IToolWindowController>()` per call and passes it to `GetController` (which caches defaults in it — a throwaway). Inline the registered→default logic without the dictionary: `if (registered.TryGetValue(type, out var c)) return c; return DefaultControllerFor(type);` — preserving the stateless per-type-default behavior (each call resolves a fresh default, no caching).
- **Verify-with:** Existing `Run_WindowManager_DefaultControllerFor` (NeoVisual.Tests:558) + `Run_WindowManager_RegisteredControllerWins` (:595) stay GREEN (the registered-wins + fresh-default behavior is unchanged). No diagnostic.
- **Fails-if:** a `Run_WindowManager_*` controller-resolution test fails (the inlined logic changed the registered-wins or fresh-default behavior); a throwaway `Dictionary` allocation remains in `ResolveController`.

### BP-22 — n14: inline ShouldLogFailure

- **Files:** `MyExtension/ToolWindows/Utils/WindowTypeProbe.cs` (delete); `MyExtension/ToolWindows/WindowManager.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** `WindowTypeProbe.ShouldLogFailure(hr) => hr < 0` (WindowTypeProbe.cs:11) is a single-use one-line wrapper. Inline `guidHr < 0` at the call site (WindowManager.cs:370) and delete the `WindowTypeProbe` class. Delete the pinning test `Run_GetGuidProperty_HResult` (NeoVisual.Tests:3249-3257) — the behavior (`hr < 0` → log `[NeoVisual] window type probe failed: 0x...`) is unchanged and the diagnostic is the contract.
- **Verify-with:** compile-RED (the class is deleted → the test referencing it fails to compile → deleted in the same step; the inlined `guidHr < 0` compiles). The `[NeoVisual] window type probe failed: 0x{hr:X8}` diagnostic is byte-stable (the behavior is identical).
- **Fails-if:** a residual `WindowTypeProbe` reference fails the build; the `window type probe failed:` diagnostic behavior changes (the inline is not `hr < 0`).

### BP-23 — n15: inline GotoDispatcher.Decide

- **Files:** `Telescope/Controller/GotoDispatcher.cs` (delete); `MyExtension/Package/MyExtensionPackage.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** `GotoDispatcher.Decide(hitCount) => hitCount == 1 ? DirectJump : OpenOverlay` (GotoDispatcher.cs:22) is a one-line wrapper. Inline the decision at the single call site (MyExtensionPackage.cs:474: `if (hits.Count == 1) { ... DirectJump ... } else { ... OpenOverlay ... }`), keep the `GotoDecision` enum, and delete the `GotoDispatcher` class. Delete the three pinning tests `Run_GotoDispatcher_SingleHitIsDirectJump`/`Run_GotoDispatcher_ZeroHitsOpensOverlay`/`Run_GotoDispatcher_MultipleHitsOpenOverlay` (Telescope.Tests:3055-3076) — the single/multi-hit behavior is pinned by the `[Telescope] goto-direct finder=... file=... line=...` diagnostic + the deferred `telescope-goto` gate (E2E-CR77-1).
- **Verify-with:** compile-RED (the class is deleted → the tests referencing it fail to compile → deleted in the same step; the inlined decision compiles). The `[Telescope] goto-direct finder=... file=... line=...` diagnostic is byte-stable (1 hit → direct jump, 0/multi → overlay).
- **Fails-if:** a residual `GotoDispatcher` reference fails the build; the goto single/multi-hit behavior changes (the inline is not `hitCount == 1`).

---

## Verification Trace

| failing test / scenario | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_GrepFinder_GetCandidatesAsync_NoBlock` (NEW) | BP-1 | `grep hits={count}` byte-stable; the pre-cancelled gather returns empty (the async override honors the token) |
| `Run_FzfFinder_BatchedFilter` (NEW) | BP-2 | `fzf hits={count}` byte-stable; `FilterCalls == 1` (ONE batched call) |
| `Run_FzfLineMapper_Boundary` (NEW) | BP-2, BP-7 | the batched output maps back to the right `(fileIndex, lineNumber)` in global order |
| `Run_FileContentCache_IOOutsideLock` (NEW) | BP-4 | a cold read does not serialize other cache accesses |
| `Run_FzfFinder_LiteralFallbackOffThread` (NEW) | BP-5 | `fzf unavailable — literal fallback` byte-stable; the fallback scan runs off-thread |
| `Run_CodeIssuesFinder_TodoScanOffThread` (NEW) | BP-6 | the TODO scan runs off-thread; `CollectErrorList` stays UI-thread (code review) |
| `Run_FileContentCache_SharedEntry` (NEW) | BP-8 | GetLines/GetContent share one entry (no re-read) |
| `Run_FzfFilter_NoSpuriousTimeoutOnCancel` (NEW) | BP-9 | the ABSENCE of `fzf filter failed: timeout after {ms}ms` on a cancelled gather |
| `Run_FzfFilter_GraceDelayToken` (NEW) | BP-9 | the grace delay is cancelled promptly (returns within ~150ms of the cancel) |
| `Run_LiteralLineScanner_Direct` (NEW) | BP-11 | n/a (coverage addition — the guards pass) |
| `Run_WindowFrameUtils_LinkedWindowIsolation` (NEW) | BP-12 | `navigate direction=...`/`navigate activated index=...` byte-stable; one stale frame is skipped, the rest survive |
| `Run_WindowNavigationEngine_Down_BelowBottom` (NEW) | BP-13 | `navigate direction=...` unchanged; the overlapping-window TARGET may change (M7) |
| `Run_WindowNavigationEngine_Down_OnePixelGapAccepted` (REWRITTEN) | BP-13 | the new Up/Down edge semantics (strictly beyond the active's edge) |
| `Run_WindowNavigationEngine_Down_ToleranceExcludes` (DELETED) | BP-14 | n/a (vacuous — deleted; the contract is pinned by BP-13) |
| `Run_RecentFilesGatherer_Dispose_RequiresUiThread` (NEW) | BP-15 | n/a (the COM unhook asserts the UI thread) |
| `Run_FocusKeeper_TickCancelled` (NEW) | BP-17 | n/a (a superseded keeper's queued tick is a no-op) |
| `Run_ToolWindowTypeResolver_FindResultsNotTextInput` (NEW) | BP-18 | n/a (FindResults1/2 are non-text-input) |
| `Run_SolutionExplorer_TryMoveCachedAcrossKeys` (NEW) | BP-19 | `toolwindow-move key=...` byte-stable; the visual-tree walk is cached across keys |
| `Run_SolutionExplorer_TryMoveSingleWalk` (existing) | BP-19 | stays GREEN (the once-per-key contract) |
| `Run_GetGuidProperty_HResult` (DELETED) | BP-22 | `[NeoVisual] window type probe failed: 0x{hr:X8}` byte-stable (the inline is `hr < 0`) |
| `Run_GotoDispatcher_*` (DELETED ×3) | BP-23 | `[Telescope] goto-direct finder=... file=... line=...` byte-stable (1 hit → direct, 0/multi → overlay) |
| m17 (ErrorListGatherer + WindowManager Dispose) | BP-15 | compile-RED + code review (the same one-line assert) |
| m19 (lParam guard) | BP-16 | compile-RED + code review (the private callback is not hermetic) |
| m48 (wrapper delete) | BP-20 | compile-RED + the updated mode-classification tests stay GREEN |
| n13 (ResolveController) | BP-21 | the existing `Run_WindowManager_*` resolution tests stay GREEN |

**Known-RED allowlist: NONE.** The baseline is all-GREEN (Telescope 297, NeoVisual 212, 44/44
e2e). The verification-agent must NOT flag the following as regressions — they are the plan's
intended diagnostic-BEHAVIOR changes (each flagged with its deferred e2e gate):
- **M2 (BP-2):** the fzf hit ORDER changes (global ranking) — `fzf hits=` count unchanged, the
  `opened fzf: file=... line=...` open-hit may differ (E2E-CR77-1).
- **M7 (BP-13):** the overlapping-window nav TARGET may change — `navigate direction=` unchanged
  (E2E-CR77-2).
- **BP-14/BP-22/BP-23:** the deleted tests (`Run_WindowNavigationEngine_Down_ToleranceExcludes`,
  `Run_GetGuidProperty_HResult`, `Run_GotoDispatcher_*`) are intentional deletions — the behavior
  they pinned is covered by the rewritten/remaining tests + the byte-stable diagnostics.

## Key decisions (the build-agent must not second-guess)

1. **M2's batched fzf uses a `\u0001` (SOH) file-boundary marker** — `"{fileIndex}\u0001{lineText}"`
   — and a new pure `FzfLineMapper.BuildCandidates`/`MapBatched` pair that preserves fzf's GLOBAL
   rank order with per-file ordinal consumption. The `IFzfEngine` interface is untouched (the
   batching is finder-side). The hit ORDER change is intended (M2).
2. **M1 keeps the sync `GetCandidates` as a delegate to `GetCandidatesAsync`** (test-only +
   empty-query path; never called on the UI thread in production — the overlay uses the async
   override). The production path has NO `.GetAwaiter().GetResult()`.
3. **M7 changes BOTH Down and Up** (`c.Y > a.Bottom` / `c.Bottom < a.Y`) and REWRITES
   `Run_WindowNavigationEngine_Down_OnePixelGapAccepted` (its old assertions pin the pre-M7
   semantics). `WindowRect.GapTo` is already correct — no change.
4. **m8+m9 are one BP step** (the same FzfFilter grace-window region); the cancelled-gather path
   mirrors the existing cancellation path (silent return, `PendingTimeoutCount--`,
   `AwaitedReadCount += 2`).
5. **m25 keeps `GrepHit` as canonical** and adds `using FzfHit = Telescope.Finders.GrepHit;`
   aliases (FzfFinder.cs, FinderColumns.cs, the test file) — a true merge with zero behavior
   change. **m48/n14/n15 are delete-with-test-update** (the pinning tests are deleted in the same
   step; the behavior is pinned by the byte-stable diagnostics).
6. **m17/m19/m21/m47** have no diagnostic; their Verify-with is the unit test + compile-RED +
   code review. m21's RED is the guard mechanism (the race is timing-dependent — the test pins
   the contract).


---


# Section B — Build Plan (Phases 2–4)

> **Session:** `neovim-planning-hub-20261006-085608` · **Plan:** 106-findings code-review fixes
> (`plans/plan.md`) · **Lane:** feature (unit-only, e2e DEFERRED to `e2e-queue.md`).
> **Scope:** Phase 2 (M5), Phase 3 (M4, n17), Phase 4 (M12, m1, m5, m16, m20, m23, m39, m42,
> m43, m44, m45, m46, n16) — **16 findings, 17 BP steps**.
> **Known-RED allowlist:** **NONE** (baseline all-GREEN — Telescope 297, NeoVisual 212, 44/44 e2e;
> per-scenario flaky counts start at 0). The verification-agent must NOT flag any of the
> pre-existing GREEN suites as regressions.
> **RED proof:** every new test fails WITHOUT the fix and passes WITH it (verify-tests-fail-without-fix).
> **No e2e as a primary Verify-with** — the e2e scenarios are QUEUED; each diagnostic-behavior
> change below is flagged with its harness site for the deferred e2e gate.

---

## Source evidence (verified file:line — spot-checked against the prior attempt's pre-verified evidence)

### M5 (Phase 2) — text-input stale-frame leak
- `MyExtension/ToolWindows/WindowManager.cs:45` — `_textInputSurfaceFocused` cached only in `OnWindowFocusChanged`.
- `WindowManager.cs:99` — `TextInputSurfaceFocused` getter (sentinel-aware).
- `WindowManager.cs:122-123` — `ComputeTextInputSurfaceFocused` fallback: `IsTextInputType && Keyboard.FocusedElement is IWpfTextView` — **cannot distinguish the Command Window's own view from the main editor** (the leak).
- `WindowManager.cs:354,384,395` — `_textInputSurfaceFocused` set only in `OnWindowFocusChanged` (never invalidated on editor focus).
- `MyExtension/ToolWindows/Utils/FocusGuard.cs:22` — `OwnsKeyboard(isInputMode, isTextInputSurface, textInputSurfaceFocused)` (pure).
- `MyExtension/Vim/VimModeTracker.cs:98-104` — `TextViewCreated` uses the **document-view discriminator**: `view.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out doc) && !string.IsNullOrEmpty(doc.FilePath)`.
- `VimModeTracker.cs:118-129` — `OnViewGotFocus` sets `_focusedView` + `_editorFocused = true` (the identity-tracking pattern).
- `MyExtension/Input/InputHandler.cs:146-169` — ctor resolves `_vsVim` (:157) and holds `_windowManager` (:151) — the natural subscription site.

### M4 / n17 (Phase 3) — shared geometric engine
- `Telescope/Overlay/Utils/FocusTargetModel.cs:275-332` — `ResolveTarget` (the pane pipeline).
- `FocusTargetModel.cs:286` — `List<Candidate> passing = new List<Candidate>()` per Ctrl+H/J/K/L move (**n17**).
- `FocusTargetModel.cs:300-303` — the `gap >= 0` filter (⇒ `allowNegativeGap=false`).
- `FocusTargetModel.cs:322` — the DPI divide dropped (band = `gap == minGap` ⇒ `divide=0`).
- `FocusTargetModel.cs:349-359` — pane `IsInDirection`: Down = `c.Y > a.Y` (:354), Up = `c.Y < a.Y` (:353) ⇒ **`strictEdge=false`**.
- `FocusTargetModel.cs:361-371` — pane `IsAligned`; `:86-112` — `PaneRect.Adjacency`/`GapTo` (formulas **identical** to `WindowRect`'s).
- `MyExtension/Navigation/WindowNavigationEngine.cs:8-51` — `SelectTarget` (the window pipeline); `:34` — `upperBound = minGap + divide`; `:53-63` — window `IsInDirection` Down = `c.Y > a.Y` (:58) — **post-M7 becomes `c.Y > a.Bottom`** ⇒ `strictEdge=true`.
- `MyExtension/Navigation/Utils/WindowRect.cs:6-56` — `WindowRect` (public readonly FIELDS `X/Y/Width/Height`; `GapTo`/`Adjacency` identical formulas).
- `MyExtension/MyExtension.csproj:32` — `<ProjectReference Include="..\Telescope\Telescope.csproj" />` — **cycle-free** (Telescope does NOT reference MyExtension) ⇒ the shared engine lives in Telescope.
- `tests/Telescope.Tests/Program.cs:4278-4289` — `NewModelAt` layout: Input=(0,60,300,40), List=(0,0,100,60), Preview=(100,0,200,60).
- `tests/Telescope.Tests/Program.cs:4321-4327` — `Run_FocusTarget_CtrlJFromListMovesToInput`: Down from List → Input. **Input.Y=60 == List.Bottom=60** ⇒ `c.Y > a.Bottom` would no-op; `c.Y > a.Y` (60 > 0) passes. **PINS `strictEdge=false` for the pane.**
- `tests/Telescope.Tests/Program.cs:4620-4627` — `Run_FocusTargetModel_DirectionTable` reflection check: `FocusTargetModel.SelectTarget` must be GONE (satisfied by the shared engine); the comment references "the direction→target table (BP-12)" — **STALE, must be updated** (BP-6).
- `tests/NeoVisual.Tests/Program.cs:2147-2261` — the ~12 `Run_WindowNavigationEngine_*` tests. **Phase 1 (M7/M11) dependency:** `Down_ToleranceExcludes` is deleted (M11), `Down_OnePixelGapAccepted` reflects the post-M7 semantics, `Down_BelowBottom` is added (M7). The shared engine's `strictEdge=true` matches the post-M7 window Down/Up.

### Phase 4 — overlay + fzf/overlay nits
- `Telescope/Overlay/TelescopeOverlay.cs:399` — `await _fzf.IsAvailableAsync()`; `:433-434` — `IsOpen = true` + `RequestShow()` **AFTER** the await (**M12**).
- `TelescopeOverlay.cs:869` — `_previewNavigator.SetText(result.Text)` unconditional per selection move (**m1**).
- `TelescopeOverlay.cs:831-835` — `results columns=`/`results count=` logged on EVERY render (**m5**).
- `TelescopeOverlay.cs:149` — `Width = 760`; `:713` — `- 18` (**m39**; `ColumnWidths.cs:41,44` — `VerticalScrollbarWidth = 18`, `DefaultOverlayWidth = 760.0`).
- `TelescopeOverlay.cs:87,1366,1376` — `_chooserMenu` assigned/nulled, never read (**m42**).
- `TelescopeOverlay.cs:46,673,713` — `_fixedWidthSum` only ever 0 (**m43**).
- `TelescopeOverlay.cs:1237-1238` — `MapKey` CtrlH/CtrlL cases (**m45**; `OverlayKeyHandler.cs:23-24` — the enum members).
- `TelescopeOverlay.cs:957` — `EnterInsert` calls `FocusPane(FocusTarget.Input)` unconditionally (**n16**).
- `Telescope/Controller/TelescopeController.cs:102-114` — `Dispose()` swallows the off-UI-thread exception and leaks the open overlay (**m20**).
- `Telescope/Logging/Utils/PaneFailureTracker.cs:14` — `_emitted` unsynchronized bool (**m23**).
- `Telescope/Overlay/Utils/Panes/IPane.cs:33` — `IsFocusable` never read by production (**m44**; test at `Program.cs:5220` reads it).
- `Telescope/Overlay/Utils/TextMotionDispatcher.cs:168` — `Apply` reports `CaretPlacement.Current` for `InsertAfter` (**m46 drift**; `PromptMotionRouter.cs:33` reports `AfterCaret`).
- `Telescope/Overlay/Utils/PromptMotionRouter.cs:23` — `ShouldConsume` calls `MapKey`; the caller's `Handle` calls `MapKey` again (:183) — **two MapKey calls per motion keystroke** (**m46**).
- `tests/Telescope.Tests/Program.cs:1873` — `Run_TextMotionDispatcher_InsertPlacements` pins the WRONG `Current` for InsertAfter (must be updated to `AfterCaret`).
- `MyExtension/Hooks/GlobalKeyboardHook.cs:86-103` — `HookCallback` never gates on `HC_ACTION` (**m16**).

---

## Build Plan

### Phase 2 — Text-input stale-frame leak (M5)

#### BP-1 — `VimModeTracker`: document-view discriminator + `MainEditorFocused` event
- **Files:** `MyExtension/Vim/VimModeTracker.cs`
- **Change:** Add `internal static bool IsMainEditorView(ITextView view)` — the document-view discriminator extracted from `TextViewCreated` (:98-104): `view.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument doc) && !string.IsNullOrEmpty(doc.FilePath)`. Add `internal event Action? MainEditorFocused`. In `OnViewGotFocus` (:118-129), after `_focusedView = view; _editorFocused = true;`, fire `MainEditorFocused` when `IsMainEditorView(view)`. This is the identity that distinguishes the main editor (document view) from the Command Window's non-document editor view.
- **Verify-with:** `Run_VimModeTracker_MainEditorFocusedEvent` (NEW, `tests/NeoVisual.Tests/Program.cs`) — a fake document `ITextView` (extend `FakeTextView` with a real `PropertyCollection` containing a fake `ITextDocument` with a non-empty `FilePath`) fires `MainEditorFocused`; a non-document view (no `ITextDocument`, or empty `FilePath`) does not. RED: today no event exists → the subscription never fires.
- **Fails-if:** `MainEditorFocused` never fires for a document view; fires for the Command Window's non-document view (the discriminator is wrong).

#### BP-2 — `WindowManager` invalidation + `InputHandler` subscription
- **Files:** `MyExtension/ToolWindows/WindowManager.cs`, `MyExtension/Input/InputHandler.cs`
- **Change:** `WindowManager` — add `internal void InvalidateTextInputSurfaceFocused()` setting `_textInputSurfaceFocused = false` (and `_focusedTextBoxInCurrentToolWindow = false`). `InputHandler` ctor (:146-169) — after `_vsVim = ResolveVimModeTracker()` (:157), subscribe `_vsVim.MainEditorFocused += () => _windowManager.InvalidateTextInputSurfaceFocused();`. The stale Command Window frame can no longer claim keyboard ownership over a focused editor.
- **Verify-with:** `Run_FocusGuard_TextInputSurfaceInvalidatedOnEditorFocus` (NEW, `tests/NeoVisual.Tests/Program.cs`) — a document-view focus runs the invalidation callback and `FocusGuard.OwnsKeyboard(isInputMode: false, isTextInputSurface: true, textInputSurfaceFocused: false)` is false (the stale frame no longer owns the keyboard). RED: today no subscription → the callback never runs.
- **Diagnostic:** `[NeoVisual] text-motion key=...` no longer fires for the main editor from a stale text-input frame (behavior change — harness sites `neovisual-textinput-motions`/`neovisual-editor-insert`, E2E-CR77-3, deferred).
- **Fails-if:** `text-motion key=...` still fires for the main editor from a stale frame; `OwnsKeyboard` still true after a document-view focus.

### Phase 3 — Geometric pipeline de-duplication (M4, n17)

#### BP-3 — NEW shared `GeometricSelectionEngine` (pure, parameterized)
- **Files:** `Telescope/Overlay/Utils/GeometricSelectionEngine.cs` (NEW; the `IGeometricRect` interface may live in the same file)
- **Change:** The pure shared engine (the `OverlayKeyHandler`/`TextMotionNavigator` pattern — dependency-free, no WPF/VS). `internal interface IGeometricRect { int X { get; } int Y { get; } int Right { get; } int Bottom { get; } bool IsEmpty { get; } }`. `internal static class GeometricSelectionEngine { public static int? SelectTarget<T>(T active, IReadOnlyList<T> candidates, int direction, bool allowNegativeGap, int divide, bool strictEdge) where T : IGeometricRect }` where `direction` is 0=Up, 1=Down, 2=Left, 3=Right. The engine computes everything from raw geometry (the `GapTo`/`Adjacency` formulas are **identical** in both surfaces — verified). Exact semantics:
  - `IsInDirection(c, a, dir)`: Up `strictEdge ? c.Bottom < a.Y : c.Y < a.Y`; Down `strictEdge ? c.Y > a.Bottom : c.Y > a.Y`; Left `c.X < a.X`; Right `c.X > a.X`.
  - `IsAligned(c, a, dir)`: Up/Down `a.X <= c.Right && c.X <= a.Right`; Left/Right `a.Y <= c.Bottom && c.Y <= a.Bottom`.
  - `GapTo(c, a, dir)`: Up `a.Y - c.Bottom`; Down `c.Y - a.Bottom`; Left `a.X - c.Right`; Right `c.X - a.Right`.
  - `Adjacency(c, a, dir)`: Up/Down (X-axis) `max(0, min(c.Right, a.Right) - max(c.X, a.X))`; Left/Right (Y-axis) `max(0, min(c.Bottom, a.Bottom) - max(c.Y, a.Y))`.
  - Filter: skip when `!IsInDirection || !IsAligned`; skip when `!allowNegativeGap && gap < 0`.
  - Closest-gap band: `gap <= minGap + divide` (divide=0 ⇒ `gap == minGap`).
  - Tie-break: `>=` (the LAST entry in iteration order wins — the pinned Cardinal rule).
  - Returns `int?` (the candidate index).
- **Verify-with:** `Run_SharedGeometricEngine_Parameters` (NEW, `tests/Telescope.Tests/Program.cs`) — the engine reproduces BOTH contracts: (a) pane params `(allowNegativeGap: false, divide: 0, strictEdge: false)` over the `NewModelAt` layout — Down from List → Input (Input.Y=60 > List.Y=0, gap=0 passes the `gap >= 0` filter), Up from Input → Preview (the pinned tie-break); (b) window params `(allowNegativeGap: true, divide: settings, strictEdge: true)` — the post-M7 Down/Up (`c.Y > a.Bottom` / `c.Bottom < a.Y`). RED: the class doesn't exist → compile error.
- **Fails-if:** the engine's output differs from either surface's pinned behavior; the `strictEdge`/`allowNegativeGap`/`divide` parameters don't reproduce the contracts.

#### BP-4 — `FocusTargetModel` delegates to the shared engine (n17 resolved)
- **Files:** `Telescope/Overlay/Utils/FocusTargetModel.cs`
- **Change:** `PaneRect` implements `IGeometricRect` (the auto-properties already match). `ResolveTarget` (:275-332) delegates to `GeometricSelectionEngine.SelectTarget(_layout, current, direction, allowNegativeGap: false, divide: 0, strictEdge: false)` and maps the returned index back to the `FocusTarget` id. DELETE the private `ResolveTarget` pipeline + the `Candidate` struct (:254-266) — the per-move `List<Candidate>` allocation (**n17**) is gone with it. The method name `SelectTarget` must NOT exist on `FocusTargetModel` (the `Run_FocusTargetModel_DirectionTable` reflection check).
- **Verify-with:** the ~30 `Run_FocusTarget_*` tests stay GREEN + `Run_FocusTargetModel_DirectionTable` passes (the reflection check `SelectTarget == null`). Diagnostic: `[Telescope] focus target=Input|List|Preview` + `focus no-op: no pane {direction} from {pane}` UNCHANGED.
- **Fails-if:** any `Run_FocusTarget_*` test fails; `FocusTargetModel.SelectTarget` still present (reflection check fails); the pinned tie-break or no-op edges change.

#### BP-5 — `WindowNavigationEngine` delegates to the shared engine
- **Files:** `MyExtension/Navigation/WindowNavigationEngine.cs`, `MyExtension/Navigation/Utils/WindowRect.cs`
- **Change:** `WindowRect` implements `IGeometricRect` (the public readonly FIELDS need interface properties, e.g. `int IGeometricRect.X => X;`). `SelectTarget` (:8-51) delegates to `GeometricSelectionEngine.SelectTarget(active, candidates, direction, allowNegativeGap: true, divide: (direction is Up/Down ? settings.YDivide : settings.XDivide), strictEdge: true)` and returns the index. The `Candidate` struct (:77-89) is deleted. **NOTE:** Phase 1 (M7) has landed — the window Down/Up are `c.Y > a.Bottom` / `c.Bottom < a.Y` (`strictEdge=true`); `Run_WindowNavigationEngine_Down_ToleranceExcludes` is deleted (M11) and `Run_WindowNavigationEngine_Down_BelowBottom` added (M7).
- **Verify-with:** the ~12 `Run_WindowNavigationEngine_*` tests stay GREEN (post-M7 semantics). Diagnostic: `[NeoVisual] navigate direction=...` / `navigate activated index=...` UNCHANGED.
- **Fails-if:** any `Run_WindowNavigationEngine_*` test fails; the window Down/Up semantics regress to `c.Y > a.Y` / `c.Y < a.Y`.

#### BP-6 — stale `Run_FocusTargetModel_DirectionTable` comment
- **Files:** `tests/Telescope.Tests/Program.cs`
- **Change:** Update the stale comment in `Run_FocusTargetModel_DirectionTable` (:4615-4627) — it references "the direction→target table (BP-12)"; the plan's decision is the **shared engine** (the `SelectTarget` method is gone, replaced by the `GeometricSelectionEngine` delegation). The reflection assertion stays (it pins `SelectTarget` gone).
- **Verify-with:** `Run_FocusTargetModel_DirectionTable` passes + the comment references the shared engine (not the fallback table).
- **Fails-if:** the comment still references the direction→target table; the reflection assertion fails.

### Phase 4 — Overlay + fzf/overlay nits (M12, m1, m5, m16, m20, m23, m39, m42, m43, m44, m45, m46, n16)

#### BP-7 — M12: `IsOpen` race in `ShowOverlayAsync`
- **Files:** `Telescope/Overlay/TelescopeOverlay.cs`
- **Change:** In `ShowOverlayAsync` (:378-449), move `IsOpen = true` + `_showState.RequestShow()` **BEFORE** the `await _fzf.IsAvailableAsync()` (:399). After the await, re-check `_showState.ShouldShowDialog()` (or `IsOpen`) and `return` (bail) if the overlay was closed during the await — so a close during the await leaves `IsOpen` false and `ShowDialog()` never fires on a closed window.
- **Verify-with:** `Run_TelescopeOverlay_IsOpenRace` (NEW, `tests/Telescope.Tests/Program.cs` — extends the `OverlayShowState` tests: `RequestShow()` → `Close()` → `ShouldShowDialog()` false, the re-check bails). Diagnostic: `[Telescope] overlay closed` fires on a close during the await; `open finder=`/`ShowDialog()` never fire after a close.
- **Fails-if:** `IsOpen` stuck true after a close during the await; `ShowDialog()` fires on a closed window.

#### BP-8 — m1: guard `_previewNavigator.SetText` on file change
- **Files:** `Telescope/Overlay/Utils/TextMotionNavigator.cs`, `Telescope/Overlay/TelescopeOverlay.cs`
- **Change:** `TextMotionNavigator` — add `internal bool SetTextIfChanged(string text)` returning true when the text changed (rebuilds the `LineIndex`) and false when unchanged (no rebuild, caret preserved). `ShowPreview` (:869) calls `SetTextIfChanged(result.Text)` instead of `SetText` — the full-file `LineIndex` rebuild (~390µs for 10k lines) is skipped when the file is unchanged.
- **Verify-with:** `Run_PreviewNavigator_SetTextGuarded` (NEW, `tests/Telescope.Tests/Program.cs`) — `SetTextIfChanged("same")` returns false and preserves the caret; `SetTextIfChanged("new")` returns true and resets the caret. Diagnostic: `[Telescope] preview file=...` UNCHANGED.
- **Fails-if:** `SetText` still called unconditionally per selection move; the `LineIndex` rebuilds on unchanged text.

#### BP-9 — m5: `results columns=`/`results count=` log on change only
- **Files:** `Telescope/Overlay/Utils/ResultsLogGate.cs` (NEW), `Telescope/Overlay/TelescopeOverlay.cs`
- **Change:** New pure `ResultsLogGate` — tracks the last-logged columns/count/selected/boxText and reports whether a change occurred. `RenderResults` (:831-835) gates the `results columns=`/`results count=` logs on `ResultsLogGate` (only log when the columns/count/selected/boxText changed — selection-only renders no longer re-log).
- **Verify-with:** `Run_ResultsLog_OnChange` (NEW, `tests/Telescope.Tests/Program.cs`) — the gate returns false for unchanged columns/count/selection, true on change. Diagnostic: `results columns={ids}` / `results count={n} selected={m} boxText={len}` fire on change only (behavior change — harness sites `telescope-results-columns` + the results-count assertions, E2E-CR77-5, deferred).
- **Fails-if:** the logs still fire on every render (selection-only renders included).

#### BP-10 — m16: gate the hook callback on `HC_ACTION`
- **Files:** `MyExtension/Hooks/GlobalKeyboardHook.cs`
- **Change:** `HookCallback` (:86) — after the `nCode < 0` pass-through (:89-92), gate on `HC_ACTION` (nCode == 0): a peeked event (`HC_NOREMOVE` = 3) is passed through untouched. Extract `internal static bool IsActionEvent(int nCode) => nCode == HC_ACTION` (the pure seam).
- **Verify-with:** `Run_GlobalKeyboardHook_HcActionGate` (NEW, `tests/NeoVisual.Tests/Program.cs`) — `IsActionEvent(HC_ACTION)` true, `IsActionEvent(HC_NOREMOVE)` false. Diagnostic: `[Hook]` lines UNCHANGED (a peeked event is never processed).
- **Fails-if:** a peeked (HC_NOREMOVE) event is processed as a real key-down.

#### BP-11 — m20: `TelescopeController.Dispose` marshals to the UI thread + closes the overlay
- **Files:** `Telescope/Controller/TelescopeController.cs`
- **Change:** `Dispose()` (:102-114) — marshal to the UI thread via `ThreadHelper.JoinableTaskFactory.Run(async () => { await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(); _overlay?.CloseOverlay(); })`; the `catch` no longer swallows the off-UI-thread path (only "already closed" is swallowed). Add an internal test seam (an injectable close-overlay delegate or an internal `CloseOverlayIfOpen()` method) so the close path is unit-testable.
- **Verify-with:** `Run_TelescopeController_DisposeClosesOverlay` (NEW, `tests/Telescope.Tests/Program.cs`) — `Dispose()` from a background thread invokes the close path (the overlay is closed, not leaked). Diagnostic: `[Telescope] overlay closed` fires when `Dispose()` runs from any thread.
- **Fails-if:** `Dispose()` off-UI-thread swallows the exception and leaks the open overlay (the modal keeps swallowing keys).

#### BP-12 — m23: interlock `PaneFailureTracker._emitted`
- **Files:** `Telescope/Logging/Utils/PaneFailureTracker.cs`
- **Change:** `_emitted` becomes an `int` (0/1) accessed via `Interlocked.Exchange(ref _emitted, 1) == 0` (net472 has no `Interlocked.Exchange(ref bool, ...)` overload) so concurrent UI/background loggers emit exactly once.
- **Verify-with:** `Run_PaneFailureTracker_Interlocked` (NEW, `tests/Telescope.Tests/Program.cs`) — N threads behind a `Barrier` call `ShouldEmit()`, exactly one returns true; the field is `int`, not `bool`. Diagnostic: `[NeoVisual] output pane unavailable: {reason}` emitted once.
- **Fails-if:** `ShouldEmit()` returns true more than once under concurrent loggers.

#### BP-13 — m39: use the centralized width constants
- **Files:** `Telescope/Overlay/TelescopeOverlay.cs`
- **Change:** `Width = 760` (:149) → `ColumnWidths.DefaultOverlayWidth`; `- 18` (:713) → `ColumnWidths.VerticalScrollbarWidth`.
- **Verify-with:** compile check + the existing `Run_ColumnWidths_*` tests stay GREEN. Diagnostic: none (no diagnostic change).
- **Fails-if:** the magic numbers remain; a column-width test fails.

#### BP-14 — m42 + m43 + m45: delete the dead overlay members
- **Files:** `Telescope/Overlay/TelescopeOverlay.cs`, `Telescope/Overlay/Utils/OverlayKeyHandler.cs`
- **Change:** Delete `_chooserMenu` (field :87, `= null` :1366, `= menu` :1376); delete `_fixedWidthSum` (field :46, reset :673, read :713 → `ActualWidth - 18`); delete `OverlayKey.CtrlH`/`CtrlL` enum members (OverlayKeyHandler.cs:23-24) + the `MapKey` CtrlH/CtrlL cases (TelescopeOverlay.cs:1237-1238 — the focus machine consumes the Ctrl chords first, so the cases are unreachable).
- **Verify-with:** compile check (no references remain) + the existing `Run_ListKeyMap_*` tests stay GREEN (they don't reference CtrlH/CtrlL). Diagnostic: none.
- **Fails-if:** a reference to `_chooserMenu`/`_fixedWidthSum`/`OverlayKey.CtrlH`/`OverlayKey.CtrlL` remains (compile error) or a test fails.

#### BP-15 — m44: delete `IPane.IsFocusable`
- **Files:** `Telescope/Overlay/Utils/Panes/IPane.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Delete `IPane.IsFocusable` (:33 — never read by production). Update `Run_IPane_Contract_Activate` (:5220 — remove the `pane.IsFocusable` assertion) + `FakePane` (:4733 — remove `IsFocusable => true`).
- **Verify-with:** `Run_IPane_Contract_Activate` updated + passes; compile check. Diagnostic: none.
- **Fails-if:** `IsFocusable` still referenced (compile error) or the updated test fails.

#### BP-16 — m46: single `MapKey` + fix the insert-placement drift
- **Files:** `Telescope/Overlay/Utils/TextMotionDispatcher.cs`, `Telescope/Overlay/Utils/PromptMotionRouter.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** `TextMotionDispatcher.Apply` (:168) reports `CaretPlacement.AfterCaret` for `InsertAfter` (matching `PromptMotionRouter` :33 — the drift). `PromptMotionRouter.ShouldConsume` (:21-53) gains `out TextMotion? motion` (the single `MapKey` result) so the caller applies via `TextMotionDispatcher.Apply(motion, nav, out placement)` — ONE `MapKey` per motion keystroke (today `ShouldConsume` calls `MapKey` at :23 and the caller's `Handle` calls it again at :183). Update `Run_TextMotionDispatcher_InsertPlacements` (:1873 — `Current` → `AfterCaret`).
- **Verify-with:** `Run_PromptMotionRouter_SingleMapKey` (NEW, `tests/Telescope.Tests/Program.cs` — one MapKey per motion keystroke + the placement drift fixed) + `Run_TextMotionDispatcher_InsertPlacements` updated + passes. Diagnostic: `[Telescope] textinput-enter-input start|end|after caret=...` reflects the fixed AfterCaret placement (behavior change — harness site `neovisual-textinput-motions`, E2E-CR77-3, deferred).
- **Fails-if:** `Apply` still reports `Current` for `InsertAfter`; `MapKey` still called twice per keystroke.

#### BP-17 — n16: no spurious `focus target=Input` on EnterInsert
- **Files:** `Telescope/Overlay/Utils/FocusTargetModel.cs`, `Telescope/Overlay/TelescopeOverlay.cs`
- **Change:** `FocusTargetModel` — add `internal bool ShouldFocus(FocusTarget target) => target != Current`. `EnterInsert` (:957) guards `FocusPane(FocusTarget.Input)` with `ShouldFocus` (only focus + log when not already on Input).
- **Verify-with:** `Run_FocusTargetModel_ShouldFocus` (NEW, `tests/Telescope.Tests/Program.cs` — `ShouldFocus(Current)` false, `ShouldFocus(other)` true). Diagnostic: `[Telescope] focus target=Input` no longer fires on EnterInsert when already on Input (behavior change — harness sites `telescope-preview`/`telescope-focus-panes`, E2E-CR77-4/5, deferred).
- **Fails-if:** `focus target=Input` still logged on EnterInsert when already on Input.

---

## Verification Trace

| finding | implicated BP steps | expected diagnostic / test |
|---|---|---|
| M5 (text-input stale-frame leak) | BP-1, BP-2 | `Run_FocusGuard_TextInputSurfaceInvalidatedOnEditorFocus` (NEW) + `Run_VimModeTracker_MainEditorFocusedEvent` (NEW); `[NeoVisual] text-motion key=...` no longer fires for the main editor from a stale frame |
| M4 (shared geometric engine) | BP-3, BP-4, BP-5, BP-6 | ~30 `Run_FocusTarget_*` + ~12 `Run_WindowNavigationEngine_*` stay GREEN + `Run_SharedGeometricEngine_Parameters` (NEW); `[Telescope] focus target=Input\|List\|Preview` + `focus no-op:` + `[NeoVisual] navigate direction=...` UNCHANGED |
| n17 (per-move `List<Candidate>`) | BP-4 | `Run_FocusTargetModel_DirectionTable` (the `SelectTarget` reflection check passes — the pipeline + allocation are gone) |
| M12 (IsOpen race) | BP-7 | `Run_TelescopeOverlay_IsOpenRace` (NEW); `[Telescope] overlay closed` on a close during the await; `open finder=`/`ShowDialog()` never fire after a close |
| m1 (SetText guard) | BP-8 | `Run_PreviewNavigator_SetTextGuarded` (NEW); `[Telescope] preview file=...` UNCHANGED |
| m5 (results log on change) | BP-9 | `Run_ResultsLog_OnChange` (NEW); `results columns={ids}` / `results count={n} selected={m} boxText={len}` fire on change only |
| m16 (HC_ACTION gate) | BP-10 | `Run_GlobalKeyboardHook_HcActionGate` (NEW); `[Hook]` lines UNCHANGED |
| m20 (Dispose marshal) | BP-11 | `Run_TelescopeController_DisposeClosesOverlay` (NEW); `[Telescope] overlay closed` fires on Dispose from any thread |
| m23 (tracker interlock) | BP-12 | `Run_PaneFailureTracker_Interlocked` (NEW); `[NeoVisual] output pane unavailable: {reason}` emitted once |
| m39 (width constants) | BP-13 | compile + `Run_ColumnWidths_*` stay GREEN |
| m42 (`_chooserMenu` dead) | BP-14 | compile (no `_chooserMenu` reference remains) |
| m43 (`_fixedWidthSum` dead) | BP-14 | compile (no `_fixedWidthSum` reference remains) |
| m44 (`IPane.IsFocusable` dead) | BP-15 | `Run_IPane_Contract_Activate` updated + passes |
| m45 (`OverlayKey.CtrlH`/`CtrlL` dead) | BP-14 | compile + `Run_ListKeyMap_*` stay GREEN |
| m46 (single MapKey + placement drift) | BP-16 | `Run_PromptMotionRouter_SingleMapKey` (NEW) + `Run_TextMotionDispatcher_InsertPlacements` updated; `[Telescope] textinput-enter-input after caret=...` reflects AfterCaret |
| n16 (spurious focus log) | BP-17 | `Run_FocusTargetModel_ShouldFocus` (NEW); `[Telescope] focus target=Input` no longer spurious on EnterInsert |

## Known-RED allowlist

- **NONE.** The baseline is all-GREEN (Telescope 297, NeoVisual 212, 44/44 e2e — the review verified both suites). Per-scenario flaky counts start at 0. The verification-agent must not flag any pre-existing GREEN suite as a regression.

## KEY DECISIONS (the build-agent must not second-guess)

1. **M4 shared engine lives in Telescope** (`MyExtension.csproj:32` references `Telescope.csproj`; cycle-free). `WindowRect` (MyExtension) and `PaneRect` (Telescope) both implement the Telescope-defined `IGeometricRect`. The engine is parameterized by **`(allowNegativeGap, divide, strictEdge)`** — the plan's original `(allowNegativeGap, divide)` was corrected by the prior attempt: the pane needs `strictEdge=false` (Down `c.Y > a.Y`, pinned by `Run_FocusTarget_CtrlJFromListMovesToInput` — Input.Y=60 == List.Bottom=60) while the window needs `strictEdge=true` (post-M7 Down `c.Y > a.Bottom`). `strictEdge` affects ONLY Down/Up; Left/Right are identical in both surfaces.
2. **M5 invalidation is keyed on the focused view's IDENTITY** (the `ITextDocument.FilePath` discriminator), NOT `IsEditorFocused` — the Command Window's own `IWpfTextView` also sets `IsEditorFocused`. The `VimModeTracker.MainEditorFocused` event + `WindowManager.InvalidateTextInputSurfaceFocused()` + `InputHandler` ctor subscription is the mechanism.
3. **m46 is a behavior fix**: `TextMotionDispatcher.Apply` reports `AfterCaret` for `InsertAfter` (matching `PromptMotionRouter`), and `Run_TextMotionDispatcher_InsertPlacements:1873` is updated from `Current` to `AfterCaret`. The `a`/`A`/`I` placement caret changes — flagged for the deferred `neovisual-textinput-motions` gate.
4. **m5 is a log-frequency behavior change**: `results columns=`/`results count=` fire on change only. The harness assertions remain satisfiable (they assert after actions that change the results/selection) — flagged for the deferred `telescope-results-columns` gate.
5. **Phase 3 assumes Phase 1 (M7/M11) has landed**: the window Down/Up are `c.Y > a.Bottom` / `c.Bottom < a.Y`; `Run_WindowNavigationEngine_Down_ToleranceExcludes` is deleted (M11) and `Run_WindowNavigationEngine_Down_BelowBottom` added (M7). The shared engine's `strictEdge=true` matches the post-M7 semantics.
6. **Doc-drift note (not a lint failure, Phase 10's m65-m74 job):** `docs/spec.md:82` describes `WindowNavigationEngine.cs` as "Pure `SelectTarget(...)` single-pass pipeline" — after M4 the method delegates to the shared engine (the name still exists, so the doc-ref lint passes). `docs/spec.md:203` + `SKILL.md:148-149` ("the collapsed `FocusTargetModel` runs the `WindowNavigationEngine` pipeline") become literally TRUE after M4.


---


# Section C — Build Plan: Phase 5 (Tool-window + forest), Phase 6 (Vim + input nits), Phase 7 (Duplication + dead code)

> **Session:** `neovim-planning-hub-20261006-085608` · **Plan:** 106-findings code-review fixes
> (`plans/plan.md`) · **Lane:** feature (unit-only, e2e DEFERRED to `e2e-queue.md`).
> **Scope:** Phase 5 (M8, m49, m50, m60), Phase 6 (m10, m11, m12, m13, m14, m15, m18, m35, m36,
> m37, m38), Phase 7 (m26, m27, m28, m29, m30, m31, m32, m33, m34, m40, m41, n19) — **27 findings,
> 28 BP steps** (m30 is split into two steps — the 4-ctor sprawl spans 4 finders + ~40 test call
> sites).
> **Known-RED allowlist:** **NONE** (baseline all-GREEN — Telescope 297, NeoVisual 212, 44/44 e2e).
> **RED proof:** every new test fails WITHOUT the fix and passes WITH it (verify-tests-fail-without-fix).
> **No e2e as a primary Verify-with** — the e2e scenarios are QUEUED; every diagnostic referenced
> below is a byte-stable existing literal (no NEW diagnostic literals in this section).

---

## Source evidence (verified file:line — from the pre-verified evidence + spot-checks below)

### Phase 5 — tool-window + forest
- `MyExtension/ToolWindows/Utils/HierarchyForestBuilder.cs:29-33` — `Build` filters the forest to
  `.cs` only (**M8**). `MyExtension/ToolWindows/Utils/HierarchyResolver.cs:49` — `FirstSourceFilePath`
  delegates to `HierarchyWalker.FirstPathEndingWith(nodes, ".cs")` — the **double** `.cs` filter
  (**m49**). `FirstPathContaining` (used by `ReturnFocusToTree` at
  `MyExtension/ToolWindows/SolutionExplorerController.cs:154`) is extension-agnostic → a query
  matching a non-`.cs` item can never resolve (**M8**).
- `MyExtension/ToolWindows/SolutionExplorerController.cs:385-422` — `MapChildren` never sets
  `Expanded=true` on child folders (only `projectNode` at :344) → a collapsed folder's
  `UIHierarchyItems` is empty, so `g` (`SelectFirstSourceFile`) only sees top-level files
  (**M8 second half**).
- `MyExtension/Package/MyExtensionPackage.cs:173` — `_errorListGatherer!` null-forgiving vs the
  `_launcher == null` guard at :169-172 (**m50**).
- `MyExtension/Input/Utils/KeybindingConfig.cs:135` — private `Merge` (defaults + user overrides)
  untested (**m60**).

### Phase 6 — vim + input nits
- `MyExtension/Hooks/GlobalKeyboardHook.cs:94` — `IsVisualStudioFocused()` runs BEFORE the
  `isKeyDown` check at :103 — every key-up pays the two Win32 calls (**m10**).
- `MyExtension/Input/InputHandler.cs:481-486` — per-key `DateTime.UtcNow` sentinel read (a no-op in
  production) (**m11**).
- `MyExtension/Vim/Utils/VimBufferSubscriptions.cs:123-141` — `DecrementRefCount` leaks
  `_bufferToTextBuffer[buffer]` on the count>1 decrement path; `VsVimModeSource.Detach` skips
  `RemoveClosed` when `lastView` is false (**m12**; the existing test `_DetachRemovesMapEntryAtZero`
  uses reflection into the private map).
- `MyExtension/Vim/Utils/VimModeSource.cs:170-188` — `OnBufferClosed` re-reads `get_VimTextBuffer`
  via reflection (`GetTextBuffer` at :398-415) on a closing buffer — a reflection failure leaks the
  `SwitchedMode` subscription (**m13**; fix = use the cached `_bufferToTextBuffer` value).
- `MyExtension/Input/Utils/KeybindingConfig.cs:242` — `ParseLeader` accepts physical modifier keys
  (`Keys.LControlKey` etc.) as a leader — accepted yet non-functional (**m14**).
  **PLAN CORRECTION:** the existing tests `Run_Keybinding_CustomLeaderParsed` (:127-132) and
  `Run_KeybindingConfig_ParseLeaderRejectsModifiers` (:152-153) PIN the old behavior (`ControlKey`
  honored) and MUST be updated in the same step.
- `MyExtension/Package/RoslynGatherers.cs:325` — `text.Lines[line-1].Start + Math.Max(0, column-1)`
  unclamped — a caret in virtual space resolves the wrong symbol (**m15**).
- `MyExtension/Hooks/Utils/KeyInjection.cs:17` — stale "only inject *arrows*" doc (the code injects
  VK_RETURN/VK_F2/VK_ESCAPE too) (**m18**).
- `MyExtension/Input/InputHandler.cs:115-135` — two near-identical `ShouldRouteToolWindowKey`
  overloads (**m35**; spot-checked below).
- `MyExtension/Input/InputHandler.cs:422` + `MyExtension/ToolWindows/Utils/FocusGuard.cs:50-52` —
  the R10 shift gate is inlined in `TryRouteToolWindowKey` while the pure 7-arg
  `FocusGuard.ShouldRouteToolWindowKey` overload is now TEST-ONLY (**m36**; spot-checked below).
- `MyExtension/Input/Utils/LeaderSequenceMatcher.cs:171` + `MyExtension/Input/Utils/SimpleShortcutMatcher.cs:27`
  — `LeaderResult.Action` / `SimpleShortcutResult.Action` are write-only dead state (`HandleKey`
  never reads `result.Action`) (**m37**).
- `MyExtension/Input/InputHandler.cs:398,403` — `CurrentController` resolved twice per key in
  `TryRouteToolWindowKey` (**m38**).

### Spot-check evidence (verified against the current tree, 2026-10-06)
- **m28 semantic diff (FinderColumns.cs):** `Files()` (:70-79) uses `DirCell(p, projectRoot)` (:76 —
  the root-tail trim); `Recent()` (:81-93) uses `Path.GetDirectoryName(h.FilePath) ?? string.Empty`
  (:90 — the full directory; the comment at :83-86 documents "a cross-solution MRU has no single
  root to trim"). The hit types ALSO differ (`Cell<FileHit>` vs `Cell<RecentFileHit>` — the
  type-disjointness guard). The merge must parameterize BOTH.
- **m35 overloads (InputHandler.cs:115-135):** the no-arg `ShouldRouteToolWindowKey()` (:115-119)
  delegates to `FocusGuard.ShouldRouteToolWindowKey(..., OwnsKeyboard)` where `OwnsKeyboard`
  resolves `_windowManager.CurrentController` internally (:104-106); the controller overload
  (:128-135) takes the controller. **BOTH are used in production** (:168, :523 controller overload;
  :547 no-arg in `ExitToolWindowInputMode`).
- **m36 shift gate (InputHandler.cs:422 + FocusGuard.cs:50-52):** the inlined gate
  `!(shift && !_windowManager.IsTextInputType && !_windowManager.IsFocusedTextBoxInCurrentToolWindow())`
  (:422) is byte-equivalent to the pure 7-arg overload's
  `!(shiftHeld && !isTextInputSurface && !textBoxFocused)` (FocusGuard.cs:50-52) — the pure overload
  has NO production callers (test-only), confirmed by grep.
- **m34 accessibility (DiagnosticLog.cs:22):** `SanitizeText` is `internal static` — the
  cross-project call from `TextMotionHelper` (MyExtension) needs it `public` or an
  `InternalsVisibleTo` extension.

### Phase 7 — duplication + dead code
- `Telescope/Finders/Utils/DefinitionHit.cs` + `ImplementationHit.cs` — byte-identical hit models
  (`FileLocation` + `SymbolName` + `Kind`) (**m26**).
- `Telescope/Overlay/Utils/FinderColumns.cs:123-141` — `Grep()`/`Fzf()` byte-identical (modulo the
  m25-merged hit type) (**m27**).
- `Telescope/Overlay/Utils/FinderColumns.cs:70-93` — `Files()`/`Recent()` near-identical; **the
  dir-cell getter DIFFERS** (`DirCell(p, projectRoot)` root-tail trim vs `Path.GetDirectoryName`
  full dir) — a real semantic diff that must be preserved (**m28**; spot-checked below).
- `Telescope/Finders/GrepFinder.cs:25`, `FzfFinder.cs:29`, `RecentFilesFinder.cs:28` — triplicated
  `HitCap = 200` (**m29**).
- `Telescope/Finders/GrepFinder.cs:51`, `FzfFinder.cs:57`, `CodeIssuesFinder.cs:56`, `FileFinder.cs:51`
  — the 4-constructor test-seam sprawl re-implemented per finder (~40 test call sites) (**m30**).
- `Telescope/Overlay/Utils/ResultColumn.cs:146` — `VisibleIdsJoined` test-only (production uses
  `ResultsFormatter.ColumnsIdList` at `TelescopeOverlay.cs:831`) (**m31**).
- `Telescope/Overlay/Utils/ResultRowCells.cs:19-29` — the 2-arg `Compute` test-only (production uses
  the 3-arg at `TelescopeOverlay.cs:737`) (**m32**).
- `MyExtension/Package/Utils/ErrorListGatherer.cs:109-147` + `Telescope/Finders/CodeIssuesFinder.cs:178-213`
  — identical per-item try/catch `ErrorItems` walk (cross-slice duplication) (**m33**).
- `MyExtension/ToolWindows/Utils/TextMotionHelper.cs:277-290` `SanitizeSample` vs
  `Telescope/Logging/Utils/DiagnosticLog.cs:22` `SanitizeText` — byte-identical, never migrated
  (**m34**; the cross-project call is legal — MyExtension references Telescope, cycle-free).
- `Telescope/Overlay/Utils/ResultColumn.cs:129,132` — `Ids`/`Catalog` dead (no production or test
  usages; the chooser iterates `_activeCatalog` directly) (**m40**).
- `Telescope/Overlay/Utils/Panes/PaneSelectionSync.cs:15` — empty shell (the n6 fix deleted `Steps`
  but left the class + a reflection test pinning its absence) (**m41**).
- `Telescope/Finders/DefinitionFinder.cs:40-71` + `ImplementationFinder.cs:41-63` — near-identical
  finder bodies (single-ctor pattern, display contract) (**n19**).

---

## Phase 5 — Tool-window + forest (M8, m49, m50, m60)

### BP-1 — M8 + m49: build the forest UNFILTERED; apply the `.cs` filter only in `FirstSourceFilePath`

- **Files:** `MyExtension/ToolWindows/Utils/HierarchyForestBuilder.cs`;
  `MyExtension/ToolWindows/Utils/HierarchyResolver.cs`;
  `MyExtension/ToolWindows/Utils/HierarchyWalker.cs` (if `FirstPathEndingWith` lives there);
  `tests/NeoVisual.Tests/Program.cs`.
- **Change:** Remove the `.cs` filter from `HierarchyForestBuilder.Build` (:29-33) so the forest
  contains ALL items (`.cs`, `.resx`, `.json`, `.xaml`, ...). `HierarchyResolver.FirstSourceFilePath`
  (:49) keeps its `HierarchyWalker.FirstPathEndingWith(nodes, ".cs")` delegation — that becomes the
  SINGLE `.cs` filter (m49: the double filter collapses to one). `FirstPathContaining` (used by
  `ReturnFocusToTree` at `SolutionExplorerController.cs:154`) is extension-agnostic — with the
  unfiltered forest, a search-box query matching a non-`.cs` item now resolves to the tree node.
- **Verify-with:** NEW `Run_HierarchyForestBuilder_Unfiltered` (NeoVisual.Tests): a forest built
  over a fake hierarchy containing `.cs` + `.resx` + `.json` items → ALL items present (RED today:
  only `.cs`). NEW `Run_HierarchyResolver_SingleCsFilter` (NeoVisual.Tests): `FirstSourceFilePath`
  returns the `.cs` path (the single filter); `FirstPathContaining` returns the non-`.cs` path.
  Diagnostic: `[NeoVisual] solution-explorer select file=...` UNCHANGED (the `g`
  select-first-source-file path still resolves `.cs`).
- **Fails-if:** `Run_HierarchyForestBuilder_Unfiltered` sees only `.cs` items (the Build filter
  survived); `Run_HierarchyResolver_SingleCsFilter` fails (the `.cs` filter is applied twice or not
  at all); `solution-explorer select file=` behavior changes.

### BP-2 — M8 (second half): `MapChildren` sets `Expanded=true` before recursing into child folders

- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** In `MapChildren` (SolutionExplorerController.cs:385-422), set `Expanded = true` on each
  child folder BEFORE recursing (mirroring the `projectNode` handling at :344) — a collapsed
  folder's `UIHierarchyItems` is empty, so `g` (`SelectFirstSourceFile`) only sees top-level files +
  already-expanded folders today. With the expansion, `g` reaches files in collapsed folders.
- **Verify-with:** NEW `Run_SolutionExplorer_MapChildrenExpandsFolders` (NeoVisual.Tests): a fake
  `UIHierarchyItem` tree with a collapsed child folder; `MapChildren` → the child folder's `Expanded`
  is true and its items are enumerated (RED today: `Expanded` stays false, the recursion skips it).
  Diagnostic: `[NeoVisual] solution-explorer select file=...` UNCHANGED (the `g` path now reaches
  deeper files).
- **Fails-if:** `Run_SolutionExplorer_MapChildrenExpandsFolders` sees `Expanded=false` on the child
  folder; `g` still only selects top-level files.

### BP-3 — m50: guard `_errorListGatherer` consistently with the `_launcher` pattern

- **Files:** `MyExtension/Package/MyExtensionPackage.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** `MyExtensionPackage.cs:173` uses `_errorListGatherer!` (null-forgiving) while the
  `_launcher` at :169-172 is guarded with `if (_launcher == null) { ... }`. Make the
  `_errorListGatherer` access consistent: null-guard it the same way (a partial finders failure must
  not degrade the whole hook path). If the package init exposes a hermetic seam, add a test; the
  guard itself is the fix.
- **Verify-with:** compile-RED + code review (the `_errorListGatherer` access is null-guarded; the
  `!` is gone). If a hermetic seam exists, NEW `Run_MyExtensionPackage_ErrorListGathererGuard`
  (NeoVisual.Tests): a null `_errorListGatherer` does not throw on the guarded path. Diagnostic:
  `[MyExtension] init <step> ok/failed: {msg}` UNCHANGED.
- **Fails-if:** `_errorListGatherer!` remains (the null-forgiving is still there); a partial finders
  failure still degrades the hook path.

### BP-4 — m60: add a `KeybindingConfig.Merge` test

- **Files:** `tests/NeoVisual.Tests/Program.cs`.
- **Change:** Test-only (no production change). `KeybindingConfig.Merge` (:135) merges the built-in
  defaults + user overrides — the core of the user-config feature — and is untested. Add a direct
  test covering: defaults-only (no user file), a user override replaces a default, a user-only
  binding is added, and the merge is deterministic.
- **Verify-with:** NEW `Run_KeybindingConfig_Merge` (NeoVisual.Tests): `Merge(defaults, null)` →
  defaults; `Merge(defaults, user)` → the user override wins for the same sequence; a user-only
  sequence is present; the merged set is deterministic (same input → same output). The test passes
  against the current code (the merge already works) — a coverage addition, not a RED.
- **Fails-if:** any `Run_KeybindingConfig_Merge` assertion fails (the merge is broken); the test is
  not registered in the runner.

**Phase 5 mid-point verify (hub checkpoint):** `dotnet run --project tests/NeoVisual.Tests` — the 4
new tests pass + the existing NeoVisual suite (212) stays GREEN. `solution-explorer select file=...`
byte-stable.

---

## Phase 6 — Vim + input nits (m10, m11, m12, m13, m14, m15, m18, m35, m36, m37, m38)

### BP-5 — m10: reorder `IsVisualStudioFocused()` after the `isKeyDown` check

- **Files:** `MyExtension/Hooks/GlobalKeyboardHook.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** In `HookCallback` (GlobalKeyboardHook.cs:94), `IsVisualStudioFocused()` runs BEFORE the
  `isKeyDown` check at :103 — every key-up pays the two Win32 calls. Reorder: check `isKeyDown`
  first, then `IsVisualStudioFocused()` (only key-downs need the focus check). Extract
  `internal static bool IsKeyDown(int wParam)` (the pure seam) if not already present.
- **Verify-with:** NEW `Run_GlobalKeyboardHook_IsKeyDownFirst` (NeoVisual.Tests): the pure seam — a
  key-up (`WM_KEYUP`) returns before the focus check (the focus-check delegate is NOT invoked); a
  key-down invokes it (RED today: the focus check runs for key-ups too). Diagnostic: `[Hook]` lines
  UNCHANGED.
- **Fails-if:** `Run_GlobalKeyboardHook_IsKeyDownFirst` sees the focus check invoked for a key-up;
  the reorder changed the key-down path.

### BP-6 — m11: avoid the per-key `DateTime.UtcNow` sentinel read

- **Files:** `MyExtension/Input/InputHandler.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** `InputHandler.cs:481-486` reads `DateTime.UtcNow` on every key-down for the 250ms
  sentinel interval — a no-op in production (the sentinel is only meaningful in tests). Guard the
  read: only compute `DateTime.UtcNow` when the sentinel is actually armed (a `_sentinelArmed` flag
  set by the test seam), or cache the last-read time and only refresh when needed.
- **Verify-with:** NEW `Run_InputHandler_NoPerKeySentinelRead` (NeoVisual.Tests): a counting clock
  seam — N key-downs with the sentinel disarmed → the clock is read 0 times (RED today: N reads);
  with the sentinel armed → the clock is read. Diagnostic: `[NeoVisual] leader-binding executed: ...`
  UNCHANGED.
- **Fails-if:** `Run_InputHandler_NoPerKeySentinelRead` sees the clock read per key-down with the
  sentinel disarmed; the sentinel behavior changes when armed.

### BP-7 — m12: fix the `_bufferToTextBuffer` leak in `DecrementRefCount`

- **Files:** `MyExtension/Vim/Utils/VimBufferSubscriptions.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** `DecrementRefCount` (VimBufferSubscriptions.cs:123-141) leaks
  `_bufferToTextBuffer[buffer]` on the count>1 decrement path — when the ref count drops but stays
  >0, the map entry is never removed, so a detached view sharing a text buffer leaves a stale entry.
  Fix: remove the map entry when the ref count reaches 0 (and on the last-view detach path),
  matching the `RemoveClosed` semantics.
- **Verify-with:** NEW `Run_VimBufferSubscriptions_NoLeak` (NeoVisual.Tests): attach 2 views to one
  buffer, detach one (count 2→1) → the map entry is still present (the buffer is still live); detach
  the second (count 1→0) → the map entry is REMOVED (RED today: the entry leaks). Existing
  `_DetachRemovesMapEntryAtZero` (which uses reflection into the private map) stays GREEN.
  Diagnostic: `vim-mode=...` UNCHANGED.
- **Fails-if:** `Run_VimBufferSubscriptions_NoLeak` sees the map entry present after the count
  reaches 0; `_DetachRemovesMapEntryAtZero` breaks (the removal path changed).

### BP-8 — m13: guard the `OnBufferClosed` reflection (use the cached `_bufferToTextBuffer` value)

- **Files:** `MyExtension/Vim/Utils/VimModeSource.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** `OnBufferClosed` (VimModeSource.cs:170-188) re-reads `get_VimTextBuffer` via
  reflection (`GetTextBuffer` at :398-415) on a closing buffer — a reflection failure leaks the
  `SwitchedMode` subscription. Fix: use the cached `_bufferToTextBuffer` value (the buffer was
  already resolved when the subscription was created) instead of re-resolving via reflection on the
  closing buffer.
- **Verify-with:** NEW `Run_VimModeSource_OnBufferClosedUsesCachedBuffer` (NeoVisual.Tests): a fake
  buffer whose reflection-based `GetTextBuffer` THROWS on close; `OnBufferClosed` → the cached value
  is used, no throw, the `SwitchedMode` subscription is removed (RED today: the reflection re-read
  throws and leaks the subscription). Diagnostic: `vim-mode=...` UNCHANGED.
- **Fails-if:** `Run_VimModeSource_OnBufferClosedUsesCachedBuffer` throws (the reflection re-read is
  still on the close path); the `SwitchedMode` subscription leaks.

### BP-9 — m14: reject physical modifier keys as a leader (PLAN CORRECTION: update the two pinning tests)

- **Files:** `MyExtension/Input/Utils/KeybindingConfig.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** `ParseLeader` (KeybindingConfig.cs:242) accepts physical modifier keys
  (`Keys.LControlKey`, `Keys.RControlKey`, `Keys.LShiftKey`, `Keys.RShiftKey`, `Keys.LMenu`,
  `Keys.RMenu`) as a leader — accepted yet non-functional (a lone modifier key can never be a
  leader). Reject them: `ParseLeader` returns null/false for a physical modifier key.
  **PLAN CORRECTION (evidence):** the existing tests `Run_Keybinding_CustomLeaderParsed` (:127-132)
  and `Run_KeybindingConfig_ParseLeaderRejectsModifiers` (:152-153) PIN the OLD behavior
  (`ControlKey` honored as a leader) and MUST be updated in this step to the new contract (a
  physical modifier key is rejected).
- **Verify-with:** NEW `Run_KeybindingConfig_RejectsPhysicalModifierLeader` (NeoVisual.Tests):
  `ParseLeader("LControlKey")` → rejected (RED today: accepted); `ParseLeader("Space")` → accepted
  (unchanged). UPDATE `Run_Keybinding_CustomLeaderParsed` + `Run_KeybindingConfig_ParseLeaderRejectsModifiers`
  to the new contract. Diagnostic: `[NeoVisual] leader-binding executed: ...` UNCHANGED (a
  physical-modifier leader never fires today anyway).
- **Fails-if:** `Run_KeybindingConfig_RejectsPhysicalModifierLeader` sees a physical modifier
  accepted; the two updated tests still pin the old `ControlKey`-honored behavior.

### BP-10 — m15: clamp the DTE `TextSelection` column to the line length

- **Files:** `MyExtension/Package/RoslynGatherers.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** `RoslynGatherers.cs:325` computes `text.Lines[line-1].Start + Math.Max(0, column-1)` —
  the column is NOT clamped to the line length, so a caret in virtual space resolves the wrong
  symbol. Fix: clamp `column` to `text.Lines[line-1].Span.Length + 1` before the offset computation.
- **Verify-with:** NEW `Run_RoslynGatherers_ColumnClamped` (NeoVisual.Tests): a `SourceText` with a
  short line; a column beyond the line length → the resolved position is clamped to the line end
  (RED today: the position is past the line end). Diagnostic: `[Telescope] references gathered
  reads=... writes=...` / `implementations gathered count=...` UNCHANGED.
- **Fails-if:** `Run_RoslynGatherers_ColumnClamped` resolves a position past the line end; a caret
  in virtual space still resolves the wrong symbol.

### BP-11 — m18: fix the stale `KeyInjection` doc

- **Files:** `MyExtension/Hooks/Utils/KeyInjection.cs`.
- **Change:** The doc at `KeyInjection.cs:17` claims "we only ever inject *arrows*" — the code now
  injects VK_RETURN/VK_F2/VK_ESCAPE too. Update the doc to list the actual injected VKs (arrows +
  VK_RETURN + VK_F2 + VK_ESCAPE).
- **Verify-with:** doc-ref lint PASS (the doc no longer contradicts the code); code review. No
  diagnostic.
- **Fails-if:** the doc still claims "only arrows"; the doc-ref lint fails on the changed comment.

### BP-12 — m35: collapse the two `ShouldRouteToolWindowKey` overloads

- **Files:** `MyExtension/Input/InputHandler.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** `InputHandler.cs:115-135` has two near-identical `ShouldRouteToolWindowKey` overloads
  (the n3 fix collapsed the call sites but left both). **Evidence (verified):** the no-arg overload
  (:115-119) delegates to `FocusGuard.ShouldRouteToolWindowKey(..., OwnsKeyboard)` where `OwnsKeyboard`
  resolves `_windowManager.CurrentController` internally (:104-106); the controller overload
  (:128-135) takes the controller and computes `FocusGuard.OwnsKeyboard(controller?.IsInputMode ==
  true, ...)` — the bodies are near-identical. **BOTH overloads are used in production:** the
  controller overload at :168 (`_routeDecision = () => ShouldRouteToolWindowKey(_windowManager.CurrentController)`)
  and :523 (`ShouldRouteToolWindowKey(c)`); the no-arg overload at :547 (`ExitToolWindowInputMode`).
  Collapse to the controller overload (the one that avoids the internal re-resolution), delete the
  no-arg overload, and update :547 to `ShouldRouteToolWindowKey(_windowManager.CurrentController)`.
- **Verify-with:** NEW `Run_InputHandler_ShouldRouteToolWindowKey_SingleOverload` (NeoVisual.Tests):
  the single overload returns the same result for the same inputs as the two did (the collapsed
  behavior is pinned). compile-RED (the deleted overload's call sites are updated). Diagnostic:
  `toolwindow-move key=...` UNCHANGED.
- **Fails-if:** a residual call to the deleted overload fails the build; the collapsed overload
  changes the routing behavior.

### BP-13 — m36: use the pure 7-arg `FocusGuard.ShouldRouteToolWindowKey` shift gate

- **Files:** `MyExtension/Input/InputHandler.cs`; `MyExtension/ToolWindows/Utils/FocusGuard.cs`;
  `tests/NeoVisual.Tests/Program.cs`.
- **Change:** `InputHandler.cs:422` inlines the R10 shift gate while the pure 7-arg
  `FocusGuard.ShouldRouteToolWindowKey` overload (FocusGuard.cs:50-52) is now TEST-ONLY. Route the
  production shift gate through the pure overload (delete the inlined copy) so the shift logic is
  single-sourced. **Evidence (verified):** the inlined gate at InputHandler.cs:422 duplicates the
  shift check the pure overload already implements (spot-checked below).
- **Verify-with:** NEW `Run_InputHandler_ShiftGateUsesPureOverload` (NeoVisual.Tests): the
  production path delegates to the pure overload (a counting seam on the pure overload — the inlined
  copy is gone). The existing FocusGuard tests stay GREEN. Diagnostic: `toolwindow-move key=...`
  UNCHANGED.
- **Fails-if:** the inlined shift gate remains (the pure overload is still test-only); the shift
  behavior changes.

### BP-14 — m37: remove the write-only `LeaderResult.Action`/`SimpleShortcutResult.Action`

- **Files:** `MyExtension/Input/Utils/LeaderSequenceMatcher.cs`;
  `MyExtension/Input/Utils/SimpleShortcutMatcher.cs`; `MyExtension/Input/InputHandler.cs`;
  `tests/NeoVisual.Tests/Program.cs`.
- **Change:** `LeaderResult.Action` (LeaderSequenceMatcher.cs:171) and `SimpleShortcutResult.Action`
  (SimpleShortcutMatcher.cs:27) are write-only — the matcher already invoked the action, and
  `HandleKey` never reads `result.Action`. Remove the `Action` member from both result types + the
  assignments.
- **Verify-with:** NEW `Run_LeaderSequenceMatcher_NoDeadAction` (NeoVisual.Tests): the result type
  has no `Action` member (compile-RED if referenced); the matcher still invokes the action (the
  existing `Run_LeaderSequenceMatcher_*` tests stay GREEN). Diagnostic: `[NeoVisual]
  leader-binding executed: ...` / `shortcut-binding executed: ...` UNCHANGED.
- **Fails-if:** a residual `result.Action` reference fails the build; the matcher stops invoking the
  action.

### BP-15 — m38: resolve `CurrentController` once in `TryRouteToolWindowKey`

- **Files:** `MyExtension/Input/InputHandler.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** `InputHandler.cs:398,403` resolves `CurrentController` twice per key in
  `TryRouteToolWindowKey` (the n3 fix addressed the double DECISION, not the double resolution).
  **Evidence (verified):** at :398 `_routeDecision()` invokes the lambda
  `() => ShouldRouteToolWindowKey(_windowManager.CurrentController)` (resolves `CurrentController`
  once), then :403 `var controller = _windowManager.CurrentController;` resolves it AGAIN. Fix:
  resolve `CurrentController` ONCE at the top of `TryRouteToolWindowKey` and pass it to both the
  routing decision and the controller variable — the `_routeDecision` seam signature changes from
  `Func<bool>` to `Func<IToolWindowController?, bool>` (its only use is :398; the test seam is
  updated to the new signature).
- **Verify-with:** NEW `Run_InputHandler_SingleControllerResolution` (NeoVisual.Tests): a counting
  controller-resolution seam — one `TryRouteToolWindowKey` call → the resolution runs ONCE (RED
  today: twice). Diagnostic: `toolwindow-move key=...` UNCHANGED.
- **Fails-if:** `Run_InputHandler_SingleControllerResolution` sees the resolution run twice; the
  routing behavior changes.

**Phase 6 mid-point verify (hub checkpoint):** `dotnet run --project tests/NeoVisual.Tests` — the 11
new/updated tests pass + the existing NeoVisual suite (212) stays GREEN. `vim-mode=...` /
`leader-binding executed: ...` / `toolwindow-move key=...` byte-stable.

---

## Phase 7 — Duplication + dead code (m26, m27, m28, m29, m30, m31, m32, m33, m34, m40, m41, n19)

### BP-16 — m26: merge the byte-identical `DefinitionHit`/`ImplementationHit`

- **Files:** `Telescope/Finders/Utils/ImplementationHit.cs` (delete);
  `Telescope/Finders/Utils/DefinitionHit.cs`; `Telescope/Finders/ImplementationFinder.cs`;
  `Telescope/Overlay/Utils/FinderColumns.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** Keep `DefinitionHit` (`FileLocation` + `SymbolName` + `Kind`) as canonical. Delete
  `ImplementationHit.cs`. Add `using ImplementationHit = Telescope.Finders.DefinitionHit;` in
  `ImplementationFinder.cs`, `FinderColumns.cs`, and the test file (the `Cell<ImplementationHit>` /
  `new ImplementationHit(...)` references resolve to `DefinitionHit`). The two classes are
  byte-identical, so the alias is a true merge. **Doc-ref propagation (rule 5a):** the bare
  `ImplementationHit` type refs (SKILL.md:92, spec.md:349, progress.md:1985) resolve via the alias
  (the lint's `Test-SymbolExists` greps the source text), but the FILE-PATH refs break: update
  `Finders/Utils/ImplementationHit.cs` → `Finders/Utils/DefinitionHit.cs` in
  `.opencode/agent/code-review-hub.md:320` + `.opencode/agent/neovim_review_hub.md:187`, and add the
  historical `Telescope/Finders/ImplementationHit.cs` (progress.md:2002) to `$intentionallyAbsent`
  in `tools/lint/check-doc-refs.ps1` (the established deleted-file pattern). The doc-ref lint is the
  acceptance gate.
- **Verify-with:** compile-RED (ImplementationHit.cs deleted → every residual `ImplementationHit`
  reference must be covered by the alias or the build fails). Existing `Run_ImplementationFinder_*`
  tests stay GREEN (the alias keeps the payload casts valid). Diagnostic: `[Telescope]
  implementations gathered count=...` / `opened implementation: file=... line=...` UNCHANGED.
- **Fails-if:** a residual `ImplementationHit` reference fails the build; an implementation-finder
  test fails (the merged model lost a member).

### BP-17 — m27: merge the byte-identical `FinderColumns.Grep()`/`Fzf()`

- **Files:** `Telescope/Overlay/Utils/FinderColumns.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** `FinderColumns.Grep()` (:123-141) and `Fzf()` are byte-identical catalogs (modulo the
  m25-merged hit type — both now use `GrepHit`). Merge into ONE method (e.g. keep `Grep()` and have
  `Fzf()` delegate) so the catalog is single-sourced.
- **Verify-with:** NEW `Run_FinderColumns_GrepFzfShared` (Telescope.Tests): the merged catalog
  returns the same columns for both finder names (RED today: two copies). Existing
  `Run_FinderColumns_*` tests stay GREEN. Diagnostic: `results columns={ids}` UNCHANGED.
- **Fails-if:** `Run_FinderColumns_GrepFzfShared` sees divergent columns; a `results columns=`
  render changes.

### BP-18 — m28: merge `FinderColumns.Files()`/`Recent()` — PRESERVE the dir-cell semantic diff

- **Files:** `Telescope/Overlay/Utils/FinderColumns.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** `Files()` (:70-93) and `Recent()` are near-identical, but the dir-cell getter DIFFERS —
  a real semantic diff that must be preserved. **Evidence (verified):** `Files()` uses
  `DirCell(p, projectRoot)` (:76 — the root-tail trim: the end folder + file name survive) while
  `Recent()` uses `Path.GetDirectoryName(h.FilePath) ?? string.Empty` (:90 — the full directory; the
  comment at :83-86 documents "a cross-solution MRU has no single root to trim"). The hit types ALSO
  differ (`Files()` uses `Cell<FileHit>`, `Recent()` uses `Cell<RecentFileHit>` — the
  type-disjointness guard). Merge the shared structure into ONE method parameterized by BOTH the
  hit type and the dir-cell getter (a `Func<object?, string>` or a bool flag), keeping both getters.
- **Verify-with:** NEW `Run_FinderColumns_FilesRecentShared` (Telescope.Tests): the merged method
  with the `Files` dir-cell getter returns the root-tail-trimmed dir; with the `Recent` getter
  returns the full directory (RED today: two copies). Existing `Run_FinderColumns_*` tests stay
  GREEN. Diagnostic: `results columns={ids}` UNCHANGED.
- **Fails-if:** `Run_FinderColumns_FilesRecentShared` shows the dir-cell getters swapped (the
  semantic diff was lost); a `results columns=` render changes.

### BP-19 — m29: one shared `HitCap` constant

- **Files:** `Telescope/Finders/GrepFinder.cs`; `Telescope/Finders/FzfFinder.cs`;
  `Telescope/Finders/RecentFilesFinder.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** `HitCap = 200` is a separate constant in three finders (GrepFinder.cs:25,
  FzfFinder.cs:29, RecentFilesFinder.cs:28). Move it to ONE shared constant (e.g.
  `FinderBase<THit>.HitCap` or a shared `FinderConstants.HitCap`) and reference it from all three.
- **Verify-with:** NEW `Run_HitCap_SharedConstant` (Telescope.Tests): the three finders reference the
  same constant (the per-finder `HitCap` fields are gone — compile-enforced). Existing
  `Run_GrepFinder_HitCapBounded` / `Run_FzfFinder_HitCapBounded` / `Run_RecentFilesFinder_*` stay
  GREEN. Diagnostic: `grep hits=` / `fzf hits=` / `recent files gathered count=` UNCHANGED.
- **Fails-if:** a per-finder `HitCap` constant remains; a hit-cap test fails (the shared constant
  changed the cap).

### BP-20 — m30a: consolidate the GrepFinder + FzfFinder test-seam ctors

- **Files:** `Telescope/Finders/GrepFinder.cs`; `Telescope/Finders/FzfFinder.cs`;
  `tests/Telescope.Tests/Program.cs`.
- **Change:** The 4-constructor test-seam sprawl (GrepFinder.cs:51, FzfFinder.cs:57,
  CodeIssuesFinder.cs:56, FileFinder.cs:51) is re-implemented per finder — the newer finders use the
  clean host-injected single-ctor pattern. Migrate `GrepFinder` + `FzfFinder` to the single-ctor
  pattern (host-injected seams), updating the ~40 test call sites in `tests/Telescope.Tests/Program.cs`.
- **Verify-with:** compile-RED (the old ctors are deleted → the test call sites must be updated).
  The existing `Run_GrepFinder_*` / `Run_FzfFinder_*` tests stay GREEN (the seams are preserved
  through the single ctor). Diagnostic: `grep hits=` / `fzf hits=` UNCHANGED.
- **Fails-if:** a residual old-ctor call fails the build; a finder test fails (a seam was lost in
  the migration).

### BP-21 — m30b: consolidate the CodeIssuesFinder + FileFinder test-seam ctors

- **Files:** `Telescope/Finders/CodeIssuesFinder.cs`; `Telescope/Finders/FileFinder.cs`;
  `tests/Telescope.Tests/Program.cs`.
- **Change:** Same migration as BP-20 for `CodeIssuesFinder` + `FileFinder` (the remaining two of
  the 4-ctor sprawl). Update the remaining test call sites.
- **Verify-with:** compile-RED (the old ctors are deleted). The existing `Run_CodeIssuesFinder_*` /
  `Run_FileFinder_*` tests stay GREEN. Diagnostic: `[Telescope] opened issue: ... line=...` /
  `opened file: ...` UNCHANGED.
- **Fails-if:** a residual old-ctor call fails the build; a finder test fails (a seam was lost).

### BP-22 — m31: delete the test-only `VisibleIdsJoined`

- **Files:** `Telescope/Overlay/Utils/ResultColumn.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** `ColumnVisibilityModel.VisibleIdsJoined` (ResultColumn.cs:146) is a dead twin of
  `ResultsFormatter.ColumnsIdList` (production uses `ResultsFormatter.ColumnsIdList` at
  `TelescopeOverlay.cs:831`). Delete `VisibleIdsJoined` + the stale doc comment; update the test that
  uses it to use `ResultsFormatter.ColumnsIdList`.
- **Verify-with:** compile-RED (the member is deleted → the test referencing it is updated in the
  same step). NEW `Run_ResultColumn_DeadMembersRemoved` (Telescope.Tests, shared with BP-26):
  `VisibleIdsJoined` is gone (compile-enforced); `ResultsFormatter.ColumnsIdList` returns the same
  joined string. Diagnostic: `results columns={ids}` UNCHANGED.
- **Fails-if:** a residual `VisibleIdsJoined` reference fails the build; `ResultsFormatter.ColumnsIdList`
  output differs.

### BP-23 — m32: verify-then-delete the 2-arg `ResultRowCells.Compute`

- **Files:** `Telescope/Overlay/Utils/ResultRowCells.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** The 2-arg `Compute` overload (ResultRowCells.cs:19-29) has no production callers
  (production uses the 3-arg at `TelescopeOverlay.cs:737`). VERIFY no production/test callers
  (grep), then delete the 2-arg overload.
- **Verify-with:** grep gate: `rg -n "Compute\(" Telescope/Overlay/Utils/ResultRowCells.cs` + the
  call sites — the 2-arg overload has 0 callers. compile-RED (the overload is deleted → any residual
  call fails the build). The existing `Run_ResultRowCells_*` tests stay GREEN (they use the 3-arg).
  Diagnostic: none.
- **Fails-if:** a residual 2-arg `Compute` call fails the build; a `Run_ResultRowCells_*` test used
  the 2-arg overload (it must be updated or the test was misread).

### BP-24 — m33: parameterize the cross-slice `ErrorItems` iteration

- **Files:** `Telescope/Finders/Utils/ErrorItemsWalker.cs` (NEW — the shared helper lives in
  Telescope; MyExtension references Telescope, cycle-free); `MyExtension/Package/Utils/ErrorListGatherer.cs`;
  `Telescope/Finders/CodeIssuesFinder.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** `ErrorListGatherer.cs:109-147` and `CodeIssuesFinder.cs:178-213` both iterate
  `ErrorItems` with the same per-item try/catch (cross-slice duplication). Extract the per-item walk
  into ONE shared helper (`ErrorItemsWalker.ForEach(ErrorItems, action)` — per-item try/catch, a
  throwing item is skipped, the rest survive) and have both call it.
- **Verify-with:** NEW `Run_ErrorItemsWalker_SharedWalk` (Telescope.Tests): the shared walker
  iterates a fake `ErrorItems` with the per-item try/catch (a throwing item is skipped, the rest
  survive). The existing `Run_ErrorListGatherer_*` / `Run_CodeIssuesFinder_*` tests stay GREEN.
  Diagnostic: `[Telescope] opened issue: ... line=...` UNCHANGED.
- **Fails-if:** `Run_ErrorItemsWalker_SharedWalk` fails (the walker's per-item isolation is wrong); a
  gatherer test fails (the walk behavior changed).

### BP-25 — m34: migrate `SanitizeSample` to `DiagnosticLog.SanitizeText`

- **Files:** `MyExtension/ToolWindows/Utils/TextMotionHelper.cs`;
  `Telescope/Logging/Utils/DiagnosticLog.cs`; `tests/NeoVisual.Tests/Program.cs`.
- **Change:** `TextMotionHelper.SanitizeSample` (:277-290) is byte-identical to the shared
  `DiagnosticLog.SanitizeText` (:22-40) — never migrated. Delete `SanitizeSample` and call
  `DiagnosticLog.SanitizeText` at the call site. **NOTE:** `TextMotionHelper` is in MyExtension;
  `DiagnosticLog` is in Telescope — MyExtension references Telescope (cycle-free), so the call is
  legal. **VERIFIED:** `DiagnosticLog.SanitizeText` is `internal static` (DiagnosticLog.cs:22) — the
  build-agent MUST make it `public` (or add the MyExtension assembly to the `InternalsVisibleTo`
  set) for the cross-project call to compile; do NOT re-copy the sanitizer.
- **Verify-with:** compile-RED (SanitizeSample deleted → the call site is updated). NEW
  `Run_TextMotionHelper_SanitizeUsesShared` (NeoVisual.Tests): the call site produces the same
  sanitized output as `DiagnosticLog.SanitizeText` (byte-identical). Diagnostic: `[NeoVisual]
  text-motion key=... caret=...` UNCHANGED.
- **Fails-if:** a residual `SanitizeSample` reference fails the build; the sanitized output differs
  (the migration changed the behavior).

### BP-26 — m40: delete the dead `ColumnVisibilityModel.Ids`/`Catalog`

- **Files:** `Telescope/Overlay/Utils/ResultColumn.cs`; `tests/Telescope.Tests/Program.cs`.
- **Change:** `ColumnVisibilityModel.Ids` (:129) and `Catalog` (:132) are dead — no production or
  test usages (the chooser iterates `_activeCatalog` directly). Delete both members.
- **Verify-with:** compile-RED (the members are deleted → any residual reference fails the build).
  NEW `Run_ResultColumn_DeadMembersRemoved` (Telescope.Tests, shared with BP-22): `Ids`/`Catalog`
  are gone (compile-enforced); the chooser's `_activeCatalog` iteration is unchanged. Diagnostic:
  `results columns={ids}` UNCHANGED.
- **Fails-if:** a residual `Ids`/`Catalog` reference fails the build; the column-chooser behavior
  changes.

### BP-27 — m41: delete the `PaneSelectionSync` shell + update its reflection test

- **Files:** `Telescope/Overlay/Utils/Panes/PaneSelectionSync.cs` (delete);
  `tests/Telescope.Tests/Program.cs`.
- **Change:** `PaneSelectionSync` (:15) is an empty shell (the n6 fix deleted `Steps` but left the
  class + a reflection test pinning its absence). Delete the class + the reflection test (the
  reflection absence check is compile-time-enforced once the class is gone). **Doc-ref propagation
  (rule 5a):** the bare `PaneSelectionSync` type refs in `docs/spec.md:117` +
  `.opencode/skills/vs-extension-dev/SKILL.md:102` (the pane-host description) MUST be removed/
  reworded (the class no longer exists — the lint's `Test-SymbolExists` fails); the historical
  progress.md refs (:873 file-path, :1008/:1046 bare) need a doc-scoped allowlist entry for
  progress.md (the `CardinalNavigationConstants` pattern) or a text update. The doc-ref lint is the
  acceptance gate.
- **Verify-with:** compile-RED (the class is deleted → the reflection test referencing it is deleted
  in the same step). NEW `Run_PaneSelectionSync_ShellRemoved` (Telescope.Tests): the type is gone
  (compile-enforced); the pane-focus behavior is pinned by the ~30 `Run_FocusTarget_*` tests.
  Diagnostic: `[Telescope] focus target=Input|List|Preview` UNCHANGED.
- **Fails-if:** a residual `PaneSelectionSync` reference fails the build; a `Run_FocusTarget_*` test
  fails (the shell's deletion changed the pane behavior).

### BP-28 — n19: merge the near-identical `DefinitionFinder`/`ImplementationFinder` bodies

- **Files:** `Telescope/Finders/DefinitionFinder.cs`; `Telescope/Finders/ImplementationFinder.cs`;
  `tests/Telescope.Tests/Program.cs`.
- **Change:** `DefinitionFinder` (:40-71) and `ImplementationFinder` (:41-63) have near-identical
  bodies (single-ctor pattern, display contract). Extract the shared gather/display logic into a
  common base (e.g. `SymbolFinderBase` or a shared helper), parameterized by the symbol-gather
  delegate; both finders delegate.
- **Verify-with:** NEW `Run_DefinitionFinder_SharedBody` (Telescope.Tests): both finders produce the
  same display contract through the shared base (RED today: two copies). Existing
  `Run_DefinitionFinder_*` / `Run_ImplementationFinder_*` tests stay GREEN. Diagnostic: `[Telescope]
  goto-direct finder=... file=... line=...` / `implementations gathered count=...` UNCHANGED.
- **Fails-if:** a definition/implementation finder test fails (the shared base changed the
  behavior); the display contract diverges.

---

## Verification Trace

| finding | implicated steps | expected diagnostic / test |
|---|---|---|
| M8 (`.cs`-only forest) | BP-1, BP-2 | `Run_HierarchyForestBuilder_Unfiltered` (NEW) + `Run_SolutionExplorer_MapChildrenExpandsFolders` (NEW); `[NeoVisual] solution-explorer select file=...` UNCHANGED |
| m49 (double `.cs` filter) | BP-1 | `Run_HierarchyResolver_SingleCsFilter` (NEW); the `.cs` filter is applied once (in `FirstSourceFilePath`) |
| m50 (`_errorListGatherer!`) | BP-3 | compile-RED + code review; `[MyExtension] init <step> ok/failed: {msg}` UNCHANGED |
| m60 (`Merge` untested) | BP-4 | `Run_KeybindingConfig_Merge` (NEW) — coverage addition |
| m10 (focus check order) | BP-5 | `Run_GlobalKeyboardHook_IsKeyDownFirst` (NEW); `[Hook]` lines UNCHANGED |
| m11 (per-key sentinel read) | BP-6 | `Run_InputHandler_NoPerKeySentinelRead` (NEW); `[NeoVisual] leader-binding executed: ...` UNCHANGED |
| m12 (`_bufferToTextBuffer` leak) | BP-7 | `Run_VimBufferSubscriptions_NoLeak` (NEW) + `_DetachRemovesMapEntryAtZero` stays GREEN; `vim-mode=...` UNCHANGED |
| m13 (`OnBufferClosed` reflection) | BP-8 | `Run_VimModeSource_OnBufferClosedUsesCachedBuffer` (NEW); `vim-mode=...` UNCHANGED |
| m14 (physical-modifier leader) | BP-9 | `Run_KeybindingConfig_RejectsPhysicalModifierLeader` (NEW) + `Run_Keybinding_CustomLeaderParsed` / `Run_KeybindingConfig_ParseLeaderRejectsModifiers` UPDATED; `leader-binding executed: ...` UNCHANGED |
| m15 (unclamped column) | BP-10 | `Run_RoslynGatherers_ColumnClamped` (NEW); `references gathered reads=... writes=...` / `implementations gathered count=...` UNCHANGED |
| m18 (stale KeyInjection doc) | BP-11 | doc-ref lint PASS (doc-only) |
| m35 (two overloads) | BP-12 | `Run_InputHandler_ShouldRouteToolWindowKey_SingleOverload` (NEW) + compile-RED; `toolwindow-move key=...` UNCHANGED |
| m36 (inlined shift gate) | BP-13 | `Run_InputHandler_ShiftGateUsesPureOverload` (NEW); `toolwindow-move key=...` UNCHANGED |
| m37 (write-only Action) | BP-14 | `Run_LeaderSequenceMatcher_NoDeadAction` (NEW) + compile-RED; `leader-binding executed: ...` / `shortcut-binding executed: ...` UNCHANGED |
| m38 (double controller resolution) | BP-15 | `Run_InputHandler_SingleControllerResolution` (NEW); `toolwindow-move key=...` UNCHANGED |
| m26 (DefinitionHit/ImplementationHit) | BP-16 | compile-RED (ImplementationHit.cs deleted) + `Run_ImplementationFinder_*` stay GREEN; `implementations gathered count=...` UNCHANGED; doc-ref lint PASS (the slice-B lists + `$intentionallyAbsent` updated) |
| m27 (Grep/Fzf catalogs) | BP-17 | `Run_FinderColumns_GrepFzfShared` (NEW); `results columns={ids}` UNCHANGED |
| m28 (Files/Recent catalogs) | BP-18 | `Run_FinderColumns_FilesRecentShared` (NEW); the dir-cell semantic diff preserved; `results columns={ids}` UNCHANGED |
| m29 (triplicated HitCap) | BP-19 | `Run_HitCap_SharedConstant` (NEW); `grep hits=` / `fzf hits=` / `recent files gathered count=` UNCHANGED |
| m30 (4-ctor sprawl) | BP-20, BP-21 | compile-RED (old ctors deleted) + the existing finder tests stay GREEN; `grep hits=` / `fzf hits=` / `opened issue: ...` / `opened file: ...` UNCHANGED |
| m31 (`VisibleIdsJoined` dead) | BP-22 | `Run_ResultColumn_DeadMembersRemoved` (NEW) + compile-RED; `results columns={ids}` UNCHANGED |
| m32 (2-arg Compute dead) | BP-23 | grep gate (0 callers) + compile-RED; the `Run_ResultRowCells_*` tests stay GREEN |
| m33 (cross-slice ErrorItems walk) | BP-24 | `Run_ErrorItemsWalker_SharedWalk` (NEW); `[Telescope] opened issue: ... line=...` UNCHANGED |
| m34 (`SanitizeSample` twin) | BP-25 | `Run_TextMotionHelper_SanitizeUsesShared` (NEW) + compile-RED; `[NeoVisual] text-motion key=... caret=...` UNCHANGED |
| m40 (`Ids`/`Catalog` dead) | BP-26 | `Run_ResultColumn_DeadMembersRemoved` (NEW, shared with BP-22) + compile-RED; `results columns={ids}` UNCHANGED |
| m41 (`PaneSelectionSync` shell) | BP-27 | `Run_PaneSelectionSync_ShellRemoved` (NEW) + compile-RED; `[Telescope] focus target=Input\|List\|Preview` UNCHANGED; doc-ref lint PASS (spec.md:117 + SKILL.md:102 + the progress.md refs updated) |
| n19 (DefinitionFinder/ImplementationFinder) | BP-28 | `Run_DefinitionFinder_SharedBody` (NEW); `goto-direct finder=... file=... line=...` / `implementations gathered count=...` UNCHANGED |

**Known-RED allowlist: NONE.** The baseline is all-GREEN (Telescope 297, NeoVisual 212, 44/44 e2e).
No scenario or test in this section is a pre-existing known-RED; the e2e scenarios are DEFERRED to
the queue (not run here), so no e2e regression can be misreported. The verification-agent must NOT
flag the following as regressions — they are the plan's intended changes:
- **BP-9 (m14):** `Run_Keybinding_CustomLeaderParsed` + `Run_KeybindingConfig_ParseLeaderRejectsModifiers`
  are UPDATED (they pinned the old `ControlKey`-honored behavior).
- **BP-16/BP-22/BP-23/BP-26/BP-27:** the deleted types/members (`ImplementationHit`,
  `VisibleIdsJoined`, the 2-arg `Compute`, `Ids`/`Catalog`, `PaneSelectionSync`) are intentional
  deletions — the behavior they pinned is covered by the remaining tests + the byte-stable
  diagnostics.

## Key decisions (the build-agent must not second-guess)

1. **m14 is a behavior change with a PLAN CORRECTION:** `ParseLeader` rejects physical modifier
   keys, and the two existing tests that PIN the old `ControlKey`-honored behavior
   (`Run_Keybinding_CustomLeaderParsed` :127-132, `Run_KeybindingConfig_ParseLeaderRejectsModifiers`
   :152-153) MUST be updated in the same step (BP-9).
2. **m28's dir-cell semantic diff is preserved:** `Files()` uses `DirCell(p, projectRoot)` (root-tail
   trim) while `Recent()` uses `Path.GetDirectoryName` (full dir) — the merged method is
   parameterized by the dir-cell getter; the getters must NOT be unified (BP-18).
3. **m30 is split into two steps** (BP-20 Grep+Fzf, BP-21 CodeIssues+FileFinder) — the 4-ctor sprawl
   spans 4 finders + ~40 test call sites; the split gives the hub a mid-change checkpoint.
4. **m33's shared helper lives in Telescope** (`ErrorItemsWalker`) — MyExtension references
   Telescope (cycle-free), so both `ErrorListGatherer` (MyExtension) and `CodeIssuesFinder`
   (Telescope) can call it. m34's cross-project call (`TextMotionHelper` → `DiagnosticLog.SanitizeText`)
   is the same direction; if `SanitizeText` is `internal`, make it `public` (or extend
   `InternalsVisibleTo`) — do NOT re-copy the sanitizer.
5. **m26 keeps `DefinitionHit` as canonical** with `using ImplementationHit = ...DefinitionHit;`
   aliases (the m25 pattern) — a true merge with zero behavior change. **m41 deletes the
   `PaneSelectionSync` shell + its reflection test** (the absence is compile-time-enforced once the
   class is gone).
6. **No NEW diagnostic literals in this section** — every referenced diagnostic is a byte-stable
   existing literal; the e2e scenarios are deferred to the queue and are NOT a primary Verify-with.
7. **Doc-ref propagation is folded into the removal steps (rule 5a):** BP-16 (m26) updates the
   `ImplementationHit.cs` file-path refs in the two agent slice-B lists + allowlists the historical
   progress.md ref; BP-27 (m41) updates the `PaneSelectionSync` refs in spec.md:117 + SKILL.md:102 +
   the historical progress.md refs. The doc-ref lint (`pwsh tools/lint/check-doc-refs.ps1`) is the
   acceptance gate for both — a removal without the doc update fails verification at the Phase 10
   gate. Note: section-d's BP-D32 (m74) also edits the same two agent slice-B lists (for
   `LinqExtensionMethods.cs` + the four omitted files) — the edits are sequential (Phase 7 before
   Phase 10), no conflict.

## Plan corrections (with evidence)

- **m14 (BP-9):** the review's fix ("reject physical modifier keys") conflicts with the existing
  tests `Run_Keybinding_CustomLeaderParsed` (:127-132) and `Run_KeybindingConfig_ParseLeaderRejectsModifiers`
  (:152-153), which PIN the old `ControlKey`-honored behavior. The plan must update both tests in
  the same step — otherwise the fix fails verification.
- **m28 (BP-18):** the review's "near-identical" framing hides a real semantic diff — the dir-cell
  getter differs (`DirCell(p, projectRoot)` root-tail trim vs `Path.GetDirectoryName` full dir). The
  merge must parameterize the getter, not unify it.
- **m34 (BP-25):** the cross-project call (`TextMotionHelper` in MyExtension → `DiagnosticLog` in
  Telescope) is legal only if `SanitizeText` is accessible from MyExtension — the step flags the
  `internal`→`public`/`InternalsVisibleTo` contingency so the build-agent does not stall on it.


---


# Section D — Build Plan: Phases 8-10 (test infra, harness, docs + lint)

> **Lane:** feature (unit-only, e2e DEFERRED to `e2e-queue.md`). RED is proven at the unit
> level only. **No e2e scenario is a primary Verify-with** — the harness steps (Phase 9)
> verify via the CHANGED ASSERTION PATTERN + the harness `-SelfCheck` (a no-VS self-check).
> The docs steps (Phase 10) verify via the doc-ref + doc-content lints PASS.
>
> **Source:** `docs/reviews/code-review.md` (2026-10-06 refresh) + `plans/plan.md`
> (the 106-findings plan, Phases 8-10). Every finding in this section is covered by ≥1 BP step.
>
> **Execution order:** one top-to-bottom pass by the build-agent (Phases 8 → 9 → 10). Phase 8
> is pure test-infra (no production behavior change except the seams it adds); Phase 9 is
> harness-only; Phase 10 is docs/lint-only. Phase 8's seam additions are the ONLY production
> source edits in this section (m52/m53/m57/m59/m61 seams + n20 comment).

## Phase 8 — Test infra (m51, m52, m53, m55, m56, m57, m58, m59, m61, n2, n3, n4, n5, n6, n7, n8, n18)

### BP-D1 — m51: make `Run_FzfFilter_KillRegisteredBeforeWrite` deterministic (no fixed `Thread.Sleep(200)`)

- **Files:** `tests/Telescope.Tests/Program.cs` (the test at :5031-5062).
- **Change:** The test currently does `Thread.Sleep(200)` (:5051) to "let the write start and
  block on the full pipe buffer", then cancels. On a slow machine the write may not have
  blocked yet, so the cancel path passes vacuously. Replace the fixed sleep with a
  **deterministic block-observed gate**: the `block.cmd` stub writes a `started.txt` marker
  before entering its infinite loop; the test polls for the marker (bounded, e.g. 5s) and
  only then cancels. This proves the write is genuinely blocked (the marker is written before
  the loop, so its presence means the child is alive and holding the pipe) without a wall-clock
  guess. Keep the `task.Wait(3000)` + `Assert.True(returned, ...)` contract unchanged.
- **Verify-with:** `Run_FzfFilter_KillRegisteredBeforeWrite` (rewritten) — the marker-poll
  replaces the `Thread.Sleep(200)`; the test passes deterministically (run 3× in a row).
  No diagnostic literal (a failure-path unit test).
- **Fails-if:** the test still contains `Thread.Sleep(200)`; or the marker-poll is absent and
  the cancel fires before the write blocks (the vacuous-pass class the finding flags).

### BP-D2 — m52: restore the real flush timer after the two FlushTimer tests (the `FakeTimer` leak)

- **Files:** `tests/Telescope.Tests/Program.cs` (:997-1055, the two FlushTimer tests);
  `Telescope/Logging/Utils/LogFileWriter.cs` (add one internal seam).
- **Change:** The two tests inject `LogFileWriter.TimerScheduler = cb => new FakeTimer()` and
  their `finally` only nulls `TimerScheduler` (:1023, :1051) — the static `_flushTimer` stays
  the injected `FakeTimer`. **Evidence (verified):** `LogFileWriter.EnsureTimer` (:219-224)
  sets `_flushTimer = TimerScheduler(Flush)` when the seam is set, and `GetWriter` (:206-209)
  only re-arms `if (_flushTimer is Timer t)` — a `FakeTimer` is never re-armed and blocks the
  real ~200ms timer for every later test. Fix: (a) in BOTH tests' `finally`, after
  `TimerScheduler = null`, call `LogFileWriter.Close()` (which disposes + nulls `_flushTimer`
  per :160-161) so the next `Write` recreates a real `Timer`; (b) add the internal seam
  `internal static bool FlushTimerIsReal => _flushTimer is Timer;` to `LogFileWriter` so the
  restore is observable without reflection.
- **Verify-with:** new test `Run_LogFileWriter_FlushTimerRestored` (self-contained, order-
  independent): `Close()` → inject `FakeTimer` via `TimerScheduler` → `Write("x")` →
  `Assert.False(LogFileWriter.FlushTimerIsReal)` → `TimerScheduler = null; Close()` (the fix)
  → `Write("x")` → `Assert.True(LogFileWriter.FlushTimerIsReal)`. No diagnostic literal.
- **Fails-if:** `FlushTimerIsReal` stays false after the restore; or a later `Write` never
  re-arms a real timer (the leak persists); or the two FlushTimer tests still leave
  `_flushTimer` as a `FakeTimer`.

### BP-D3 — m53: replace the 7 reflection-coupled assertions in `tests/Telescope.Tests/Program.cs` with seams

- **Files:** `tests/Telescope.Tests/Program.cs` (:4624, :4714, :5003, :5018, :3880, :3939,
  :5343); `Telescope/Finders/Utils/FileContentCache.cs`; `Telescope/Finders/GrepFinder.cs`,
  `FzfFinder.cs`, `CodeIssuesFinder.cs`.
- **Change:** Replace each reflection site with a public/internal seam:
  - **:3880 (`_lru` field presence) + :3939 (`ReadEntries`/`ReadEntryCount` reading `_entries`):**
    add `internal int EntryCount => _entries.Count;` and
    `internal IReadOnlyList<string> LruOrder` (a read-only view of `_lru`, MRU-first) to
    `FileContentCache`. Rewrite `Run_FileContentCache_Lru` to assert `LruOrder` directly
    (the touch-the-oldest-survives contract) and `ReadEntryCount` → `cache.EntryCount`.
  - **:5343 (`ReadContentCache` reading `_contentCache`):** add `internal FileContentCache
    ContentCache => _contentCache;` to `GrepFinder`/`FzfFinder`/`CodeIssuesFinder`; rewrite
    `Run_FileContentCache_Shared` to use `grep.ContentCache` etc. (delete `ReadContentCache`).
  - **:4624 (`SelectTarget` absence), :4714 (`Steps` absence), :5003 (`PaneNavigationEngine`
    absence), :5018 (`_active` absence):** these pin DELETIONS that are compile-time-enforced
    (any reference to the deleted member fails to compile) and whose post-deletion behavior is
    already pinned by the ~30 `Run_FocusTarget_*` / `Run_PaneHost_*` behavior tests. Replace
    the reflection-based absence assertion with a **public capability seam** where the class
    can expose one: `FocusTargetModel` gains `internal static bool UsesSharedGeometricEngine`
    (true after the Phase-3 M4 shared-engine extraction — the direction-table test asserts it
    instead of `GetMethod("SelectTarget")`); `PaneHost` gains `internal bool StoresActiveField
    => false` (asserted instead of `GetField("_active")`); for `PaneSelectionSync.Steps` and
    `PaneNavigationEngine`, the type/member is gone so the test asserts the PUBLIC contract
    (the inlined call site behavior) and the deletion is compile-time-enforced — the
    reflection absence check is DELETED and the behavior assertions retained.
- **Verify-with:** the rewritten tests pass (`Run_FocusTargetModel_DirectionTable`,
  `Run_PaneSelectionSync_StepsRemoved`, `Run_FocusTargetModel_IsSingleResolver`,
  `Run_PaneHost_SingleOwner`, `Run_FileContentCache_Lru`, `Run_FileContentCache_Shared`) +
  a grep gate: `rg -n "GetField|GetMethod" tests/Telescope.Tests/Program.cs` returns only the
  documented-kept sites (none of the 7). No diagnostic literal.
- **Fails-if:** any of the 7 reflection calls remains; or a rewritten test fails because the
  seam does not exist (CS0117/CS1061); or the behavior assertions regress.

### BP-D4 — m55: remove the two old reflection tests in `tests/NeoVisual.Tests/Program.cs`

- **Files:** `tests/NeoVisual.Tests/Program.cs` (:608-647 `Run_WindowManager_DefaultControllerCache_ReturnsCachedInstance`,
  :3160-3190 `Run_GetController_SameInstance`).
- **Change:** Delete both tests. They are the m17 residual: they drive the private
  `WindowManager.GetController` via reflection + the `uiThreadDispatcher` mutation. The
  hermetic `Run_WindowManager_DefaultControllerCache_NoReflection` (:3631-3658) already pins
  the same per-type-cache contract through the internal `GetController` seam (no reflection
  into the method). Deleting the two old tests removes the reflection coupling without losing
  coverage.
- **Verify-with:** the suite still passes with the two tests gone (the contract is pinned by
  `Run_WindowManager_DefaultControllerCache_NoReflection` + `Run_WindowManager_PerTypeDefaultsDoNotLeakInputMode`).
  No diagnostic literal.
- **Fails-if:** either test still exists; or the remaining hermetic test fails (the contract
  was NOT actually pinned elsewhere).

### BP-D5 — m56: collapse the three duplicate FocusGuard assertions

- **Files:** `tests/NeoVisual.Tests/Program.cs` (:1435-1443, :1505-1516, :1518-1528).
- **Change:** Each of the three tests asserts the IDENTICAL expression twice with different
  messages (the N46 "one assertion per case" fix missed these three). Collapse each to ONE
  assertion (keep the more descriptive message):
  - `Run_FocusGuard_NonToolWindowBlocks` (:1437-1442): one `Assert.False(...)`.
  - `Run_FocusGuard_TextInputSurfaceFocused_EditorFocusedNotFocusedSurface` (:1510-1515): one
    `Assert.False(...)`.
  - `Run_FocusGuard_TextInputSurfaceFocused_GenuinelyFocusedOwnsKeyboard` (:1522-1527): one
    `Assert.True(...)`.
- **Verify-with:** the three tests pass with a single assertion each (grep the file: no
  `Run_FocusGuard_*` test body contains two identical `FocusGuard.ShouldRouteToolWindowKey`
  calls). No diagnostic literal.
- **Fails-if:** any of the three still asserts the identical expression twice.

### BP-D6 — m57: `Run_BlockCaretState_DesiredVsRendered` uses the seam (no reflection, no `GetUninitializedObject`)

- **Files:** `MyExtension/Adornments/BlockCaretAdornment.cs`; `tests/NeoVisual.Tests/Program.cs` (:3397-3422).
- **Change:** Extract the pure state model from `BlockCaretAdornment` into a dependency-free
  class `BlockCaretState` (the `OverlayKeyHandler`/`TextMotionNavigator` pattern):
  `bool DesiredActive { get; set; }`, `bool RenderedActive { get; private set; }`,
  `void ApplyRendered(bool value)` (no-op when unchanged), `void OnLostFocus() =>
  ApplyRendered(false)`, `void OnGotFocus() => ApplyRendered(DesiredActive)`. `BlockCaretAdornment`
  delegates its `_desiredActive`/`_renderedActive` + `OnLostFocus`/`OnGotFocus` bodies to the
  state (the `block-caret active=` diagnostic stays logged from the adornment's `ApplyRendered`
  wrapper — literal unchanged). Rewrite the test to drive `BlockCaretState` directly (set
  `DesiredActive`, call `OnLostFocus`/`OnGotFocus`, assert `RenderedActive`) — no reflection,
  no `FormatterServices`.
- **Verify-with:** `Run_BlockCaretState_DesiredVsRendered` (rewritten, seam-driven) passes;
  the `[NeoVisual] block-caret active=True|False` diagnostic literal is unchanged (grep the
  source). No new diagnostic literal.
- **Fails-if:** the test still uses `GetField`/`GetUninitializedObject`; or `BlockCaretState`
  does not exist (CS0246); or the focus-loss/regain contract (rendered cleared, desired kept,
  rendered restored) regresses.

### BP-D7 — m58 + n3: DOCUMENT the per-test-timeout accepted risks (no code change)

- **Files:** `tests/TestRunner.cs` (the comment at :57-68).
- **Change:** The comment at :57-68 already documents both risks: (a) m58 — "A timed-out test
  leaves an abandoned thread that may mutate static state (e.g. ThreadHelper.uiThreadDispatcher);
  that risk is documented and accepted (killing the process would abort the whole suite)";
  (b) n3 — the timeout is a "stop waiting" report ("reported as FAILED ('timed out after Ns')
  and the runner CONTINUES"), not a kill. Verify the comment names BOTH explicitly; if either
  is missing, extend the comment to state it verbatim. No code change.
- **Verify-with:** the comment at `tests/TestRunner.cs:57-68` contains both the abandoned-
  thread static-mutation risk (m58) and the "stop waiting, not a kill" statement (n3); the
  suite still passes. No diagnostic literal.
- **Fails-if:** the comment omits either risk; or a code change was made to the timeout
  mechanism (this finding is DOCUMENT-only).

### BP-D8 — n2: add the stack trace to failure output

- **Files:** `tests/TestRunner.cs` (:101).
- **Change:** `Console.WriteLine($"FAIL  {method.Name}: {Unwrap(testError).Message}")` prints
  only the message. Change to print the full exception (message + stack trace):
  `Console.WriteLine($"FAIL  {method.Name}: {Unwrap(testError)}")` (the exception's `ToString()`
  includes the stack trace). Extract the formatting into a pure seam
  `internal static string FormatFailure(string methodName, Exception ex)` so it is unit-testable.
- **Verify-with:** new test `Run_Assert_StackTrace` (in `tests/NeoVisual.Tests/Program.cs`):
  `TestRunner.FormatFailure("X", new InvalidOperationException("boom"))` contains both `boom`
  and a stack-trace frame (`at ` / `InvalidOperationException`). No diagnostic literal.
- **Fails-if:** `FormatFailure` output lacks the stack trace; or the runner still prints only
  `.Message`.

### BP-D9 — n4: delete the dead `PositiveActionKeyCount` constant

- **Files:** `tests/NeoVisual.Tests/Program.cs` (:1392).
- **Change:** Delete `private const int PositiveActionKeyCount = 7;` — it is dead (the FocusGuard
  tests assert the guard's own return value, not the count; the comment at :1390-1391 confirms
  "Guard only checks > 0"). Verify no remaining reference.
- **Verify-with:** the file compiles with the constant deleted (grep: no `PositiveActionKeyCount`
  reference remains); the suite passes. No diagnostic literal.
- **Fails-if:** the constant still exists; or a reference breaks the build (CS0103).

### BP-D10 — n5: split the eager `Run_KeyNames_RoundTrip_WindowPrefix`

- **Files:** `tests/NeoVisual.Tests/Program.cs` (:356-393).
- **Change:** The test runs three scenarios in one method (w,- / w,| / w,d). Split into three
  focused tests: `Run_KeyNames_RoundTrip_WindowPrefix_SplitBelow` (w,-), `..._SplitRight`
  (w,|), `..._CloseWindow` (w,d) — each with its own matcher + one `Assert.Equal(1, executed)`.
  Keep the round-trip contract (Space, w, physical key → exact config sequence → fires).
- **Verify-with:** the three split tests pass; `--list` shows the three names. No diagnostic
  literal.
- **Fails-if:** the eager three-in-one test remains; or a split test fails (the round-trip
  contract regresses).

### BP-D11 — n6 + M11: fix the stale Down-tolerance comments (coordinate with the Phase-1 M11 deletion)

- **Files:** `tests/NeoVisual.Tests/Program.cs` (:2159-2173, the Down-tolerance tests).
- **Change:** Phase 1 (M11) deletes the vacuous `Run_WindowNavigationEngine_Down_ToleranceExcludes`
  and keeps `Run_WindowNavigationEngine_Down_OnePixelGapAccepted` (which pins the no-tolerance
  contract). This step fixes the STALE COMMENTS in the surviving Down tests that still describe
  the removed `> 1` tolerance: rewrite them to describe the actual contract (`c.Y > a.Y` today;
  after the Phase-1 M7 fix, `c.Y > a.Bottom` for Down / `c.Bottom < a.Y` for Up — strictly
  beyond the active's edge, no tolerance). No behavior change in this step.
- **Verify-with:** the surviving Down-tolerance test comments match the post-M7 contract (no
  `> 1` / "tolerance" wording); the tests pass. No diagnostic literal.
- **Fails-if:** a comment still claims the `> 1` tolerance; or the M11 deletion was not applied
  (the vacuous test still exists).

### BP-D12 — n7: replace `Assert.True(x == y, "msg")` with `Assert.Equal` (20 sites)

- **Files:** `tests/NeoVisual.Tests/Program.cs` (the ~20 sites, starting at :563).
- **Change:** Replace every `Assert.True(x == y, "msg")` / `Assert.False(x == y, "msg")` with
  `Assert.Equal(y, x)` / `Assert.NotEqual(y, x)` so a failure prints actual/expected (the
  shared `Assert.Equal` at `tests/TestRunner.cs:126-132` already formats
  `Expected [..] but got [..]`). Do NOT touch `Assert.True(bool)`/`Assert.False(bool)` sites
  that assert a boolean condition (not an equality).
- **Verify-with:** new test `Run_Assert_Equal` (in `tests/NeoVisual.Tests/Program.cs`):
  `Assert.Equal(1, 2)` throws with a message containing both `1` and `2`; plus a grep gate:
  `rg -n "Assert\.True\([^)]*==|Assert\.False\([^)]*==" tests/NeoVisual.Tests/Program.cs`
  returns 0. No diagnostic literal.
- **Fails-if:** any `Assert.True(x == y)` equality site remains; or `Run_Assert_Equal` fails
  (the shared `Assert.Equal` message lacks actual/expected).

### BP-D13 — n8: shorten the `Run_TestRunner_Timeout` sleep (do NOT delete)

- **Files:** `tests/NeoVisual.Tests/Program.cs` (:3679-3725).
- **Change:** The test's `SleepingTests.Run_Sleeps` blocks far beyond the nested 5s timeout,
  adding ~5s to the suite and leaving a 10-minute sleeping thread alive. Shorten the nested
  sleep so the timeout still fires but the abandoned thread dies quickly: change `Run_Sleeps`
  to sleep ~6s (just past the nested 5s timeout) instead of 10 minutes. Keep the test (it is
  the ONLY timeout-mechanism test — n8 says shorten, do NOT delete). The nested runner's
  `perTestTimeoutSeconds: 5` stays.
- **Verify-with:** `Run_TestRunner_Timeout` passes and completes in ~6s (not ~5s + a 10-minute
  thread); the `FAIL  Run_Sleeps` + `PASS  Run_After` assertions still hold. No diagnostic
  literal.
- **Fails-if:** the test is deleted; or `Run_Sleeps` still sleeps 10 minutes; or the timeout
  no longer fires (the nested sleep is shorter than the 5s timeout).

### BP-D14 — n18: KEEP + document `Run_LineIndex_LineOfMatchesNavigator` (the only cross-implementation check)

- **Files:** `tests/Telescope.Tests/Program.cs` (:2061-2084).
- **Change:** No code change. Add a comment above the test documenting that it is the ONLY
  cross-implementation check in the suite (it asserts `LineIndex.LineOf` equals
  `TextMotionNavigator.LineNumber` across 7 inputs — two independent implementations of the
  same line-number semantics), so it is deliberately KEPT despite the shared-defect caveat
  (n18). The comment must state why it is retained.
- **Verify-with:** the comment is present; the test still passes. No diagnostic literal.
- **Fails-if:** the test is deleted or weakened; or the documenting comment is absent.

### BP-D15 — m59: `Run_IsKeyOfInterest_OverlayOpenShortCircuit` uses the seam (no `GetUninitializedObject`/reflection)

- **Files:** `Telescope/Controller/TelescopeController.cs`; `tests/NeoVisual.Tests/Program.cs` (:2872-2916).
- **Change:** Add an internal test seam to `TelescopeController`:
  `internal void SetOverlayOpenForTest(bool open)` that sets `_overlay` to a minimal
  non-WPF overlay stub (a private nested `FakeOverlay` with `IsOpen => true`) or null — so
  `IsOpen` is drivable hermetically without `FormatterServices.GetUninitializedObject` or
  reflection into `_overlay`/`TelescopeOverlay.IsOpen`. Rewrite the test to construct a real
  `InputHandler` via its ctor (which takes the `TelescopeController` — `InputHandler.cs:149`)
  and drive `IsKeyOfInterest` with the seam toggling `IsOpen` true/false. The `_leaderMatcher`/
  `_leaderKey`/`_lastSentinelRefresh` fields are set by the real ctor (no reflection).
- **Verify-with:** `Run_IsKeyOfInterest_OverlayOpenShortCircuit` (rewritten, seam-driven)
  passes; grep: the test no longer uses `FormatterServices` or `GetField`. No diagnostic
  literal.
- **Fails-if:** the test still uses `GetUninitializedObject`/`GetField`; or `SetOverlayOpenForTest`
  does not exist (CS0117); or the overlay-open short-circuit contract regresses.

### BP-D16 — m61: replace the 8 `ThreadHelper.uiThreadDispatcher` reflection mutations with a shared seam

- **Files:** `tests/NeoVisual.Tests/Program.cs` (:623, :2330, :3163, :3342, :3467, :3529,
  :3607, :3640); `tests/TestRunner.cs` (add the seam to `TestScaffold`).
- **Change:** Add a shared `IDisposable` seam to `TestScaffold`:
  `public static IDisposable SetCurrentDispatcherAsUiThread()` that (a) reads the private
  static `ThreadHelper.uiThreadDispatcher` field ONCE (the single reflection site), (b) sets it
  to `Dispatcher.CurrentDispatcher`, and (c) returns an `IDisposable` whose `Dispose()` restores
  the original — so the restore is guaranteed by a `using` even on exception (the current
  try/finally restore is correct but the reflection is duplicated 8×; the m58 timeout risk is
  documented in BP-D7). Rewrite the 8 tests to `using (TestScaffold.SetCurrentDispatcherAsUiThread()) { ... }`.
- **Verify-with:** the 8 tests pass; grep gate: `rg -n "uiThreadDispatcher" tests/NeoVisual.Tests/Program.cs`
  returns 0 (the only reflection site is inside `TestScaffold` in `tests/TestRunner.cs`). No
  diagnostic literal.
- **Fails-if:** any of the 8 tests still mutates the field via reflection; or the seam's
  restore does not run on exception (a later test sees a stale dispatcher).

## Phase 9 — Harness (M9, M10, m62, m63, m64, n9)

> Verify-with for this phase = the CHANGED ASSERTION PATTERN + `pwsh tools/harness/test-e2e.ps1 -SelfCheck`
> (parse + helper invariants + `Assert-SeedConsistent`, no VS). No e2e scenario is a primary
> Verify-with (the scenarios are deferred to the queue).

### BP-D17 — M9: fix the `telescope-goto` re-walk trigger assert (the unsatisfiable cursor-advance)

- **Files:** `tools/harness/test-e2e.ps1` (:2027).
- **Change:** **Evidence (verified):** `Wait-LogLine` (`harness-common.ps1:186`) starts at
  `$start = [Math]::Max($fromIndex, $script:LogSearchedTo)` and on every no-match scan advances
  the persistent cursor (`:193 $script:LogSearchedTo = $script:LogCache.Count`). In
  `telescope-goto` Part 1, the 15s `Wait-NewLogLineAfter` for `goto-direct` (:2020) times out on
  the 0-gather race and advances the cursor PAST the `definitions gathered count=0` line; the
  trigger assert at :2027 reuses the same `$idx` snapshot but searches from the advanced cursor,
  so it always throws "never saw: first goto attempt gathered 0 definitions". Fix: replace the
  `Assert-NewLogLineAfter` at :2027 with a **direct LogCache window scan** in the `[idx, count)`
  window (the `Assert-NoEnterStorm` idiom at `harness-common.ps1:235-244` — scans
  `$script:LogCache` from a fixed index to `$script:LogCache.Count` WITHOUT advancing the
  cursor): `Update-LogCache $logPath; $saw = $false; for ($i = $idx; $i -lt $script:LogCache.Count; $i++) { if ($script:LogCache[$i] -match "$($script:PfxTel)definitions gathered count=0") { $saw = $true; break } }; if (-not $saw) { throw 'never saw: first goto attempt gathered 0 definitions (the re-walk trigger)' }`.
  NOTE: the review's alternative "take a fresh `Get-LogCacheIndex` snapshot" is INCORRECT as
  literally stated — `Get-LogCacheIndex` returns the cache END (`harness-common.ps1:96-101`),
  which is past the 0-gather line; the correct variant is the direct window scan (or resetting
  `$script:LogSearchedTo = $idx` before the assert).
- **Verify-with:** the changed assertion pattern (the direct window scan replaces the
  `Assert-NewLogLineAfter` at :2027) + `pwsh tools/harness/test-e2e.ps1 -SelfCheck` PASS.
- **Fails-if:** the `Assert-NewLogLineAfter` at :2027 remains (the unsatisfiable cursor-advance
  persists); or the window scan uses the advanced cursor (still misses the 0-gather line).

### BP-D18 — M10: make `Run_FzfFilter_TimeoutRace` deterministic (no wall-clock `ping` boundary)

- **Files:** `Telescope/Filter/FzfFilter.cs`; `tests/Telescope.Tests/Program.cs` (:1289-1318).
- **Change:** The stub `ping -n 2 127.0.0.1` (≈1s) races `FilterTimeoutMs=1000` — a wall-clock
  boundary race. Add an internal seam to `FzfFilter`:
  `internal Func<int, CancellationToken, Task>? DelayFactory;` and route BOTH `Task.Delay`
  calls (:233 the timeout, :265 the grace) through `DelayFactory?.Invoke(...) ?? Task.Delay(...)`.
  Rewrite the test to inject a `DelayFactory` returning a task the test controls (a
  `TaskCompletionSource`): the stub emits immediately; the test completes the timeout task at
  the boundary (simulating `winner == timeout` with `all.IsCompleted`) and asserts the filtered
  output wins (not the unfiltered list), `PendingTimeoutCount == 0`, and no spurious
  `fzf filter failed: timeout after` line. Fully deterministic — no real subprocess timing.
- **Verify-with:** `Run_FzfFilter_TimeoutRace` (rewritten, seam-driven) passes deterministically
  (run 3× in a row); the `fzf filter failed: timeout after {ms}ms` literal is unchanged (the
  test asserts its ABSENCE on the boundary-completion path). No new diagnostic literal.
- **Fails-if:** the test still uses `ping`/wall-clock; or `DelayFactory` does not exist
  (CS0117); or the boundary fast path returns the unfiltered list (the m11 bug regresses).

### BP-D19 — m62: drop the presence-only `preview tokens=\d+` assertion

- **Files:** `tools/harness/test-e2e.ps1` (:1596).
- **Change:** The assertion accepts `tokens=0` (the count reads 0 for BOTH buffer sources per
  the documented known limitation at :1589-1595), so it pins nothing. DROP the
  `Assert-NewLogLine $logPath "$($script:PfxTel)preview tokens=\d+"` line. The `preview file=.*\.cs`
  assertion (:1588) already pins the preview loaded content. The `[Telescope] preview tokens=...`
  diagnostic in the SOURCE is UNCHANGED (only the harness assertion is removed).
- **Verify-with:** the `tokens=` assert is gone from the `telescope-preview` scenario body
  (grep `tools/harness/test-e2e.ps1` for `preview tokens=` returns 0) + `-SelfCheck` PASS.
- **Fails-if:** the `tokens=` assertion remains; or the source `preview tokens=` diagnostic was
  removed (it must stay — only the harness assert is dropped).

### BP-D20 — m63: make the four severity-nav outcome assertions deterministic (no runtime branching)

- **Files:** `tools/harness/test-e2e.ps1` (:1010-1014, :1022-1026, :1034-1038, :1040-1044).
- **Change:** The four `if ($hasErrors) { target form } else { no-op form }` branches silently
  take the no-op branch when the Error List poll times out with warnings==0. The build-settle
  gate (:954-960) + the poll (:967-978) already wait for BOTH errors AND warnings > 0, but a
  poll timeout falls through with `$hasWarnings=false`. Fix: after the poll, THROW if NOT
  (`$diag.Errors -gt 0 -and $diag.Warnings -gt 0`) — the deterministic `DiagProbe.cs` seed
  (3 errors + 3 warnings, :983-992) MUST produce both; a timeout means the Error List did not
  settle, which must FAIL the scenario, not branch to no-op. Then remove the `if ($hasErrors)`/
  `if ($hasWarnings)` branches — the four assertions become UNCONDITIONAL target-form asserts
  (`diagnostic-nav direction=next|prev severity=error|warning target=.*DiagProbe\.cs line=\d+`).
- **Verify-with:** the changed assertion pattern (the four target-form asserts are
  unconditional; the `if ($hasErrors)`/`if ($hasWarnings)` branches are gone) + `-SelfCheck`
  PASS. The `[NeoVisual] diagnostic-nav direction=... severity=... target=...` /
  `diagnostic-nav no-op:` literals are unchanged.
- **Fails-if:** any `if ($hasErrors)`/`if ($hasWarnings)` branch remains; or the poll timeout
  still falls through to the no-op branch instead of throwing.

### BP-D21 — m64: make `explorer-open-navigation`'s `editor-view-opened` assertion order-independent

- **Files:** `tools/harness/test-e2e.ps1` (:1467).
- **Change:** `Assert-NewLogLine $logPath "$($script:PfxNeo)editor-view-opened file=.*\.cs"`
  fails when the first source file was ALREADY open (activating an existing view raises no
  `TextViewCreated`). Make it order-independent: capture the selected path from the
  `solution-explorer select file=.*\.cs` line (:1466), then assert the file is the ACTIVE
  document via `Wait-ActiveDocumentMatch` (works whether or not it was pre-opened) instead of
  the `editor-view-opened` log line. Keep the `solution-explorer select file=` assertion
  (deterministic) + the `o` → `solution-explorer open` assertion (:1470).
- **Verify-with:** the changed assertion pattern (the `editor-view-opened` assert is replaced
  by a `Wait-ActiveDocumentMatch` on the selected file) + `-SelfCheck` PASS. The
  `[NeoVisual] solution-explorer select file=...` / `solution-explorer open` literals are
  unchanged.
- **Fails-if:** the `editor-view-opened file=.*\.cs` assert remains (order-dependent); or the
  active-document check fails when the file was pre-opened.

### BP-D22 — n9: replace the fixed 1200ms sleeps in `iterate-telescope.ps1` with polls

- **Files:** `tools/harness/iterate-telescope.ps1` (:333, :338).
- **Change:** Replace `Start-Sleep -Milliseconds 1200` at :333 ("allow focus + initial render")
  with a poll for the prompt-focused state (the `Focus prompt => True, mode=insert` log line,
  bounded ~15s); replace the :338 sleep ("allow fzf to run") with a poll for the
  `results count=` / `promptChanged` log line (bounded ~15s). The overlay-open poll at :318
  already exists — the :333 sleep is redundant with it.
- **Verify-with:** the changed pattern (both fixed sleeps replaced by bounded `Wait-LogContains`/
  `Wait-NewLogLine` polls) + the script parses (`pwsh -NoProfile -Command '...ParseFile...'` or
  the harness `-SelfCheck` if it covers this script). No diagnostic literal.
- **Fails-if:** either fixed 1200ms sleep remains; or a poll has no bound (can hang).

## Phase 10 — Docs + lint (m65, m66, m67, m68, m69, m70, m71, m72, m73, m74, n1, n10, n11, n12, n20)

> Verify-with for this phase = `pwsh tools/lint/check-doc-refs.ps1` PASS (0 unresolved) +
> `pwsh tools/lint/check-doc-content.ps1` PASS (12/12). No e2e, no unit tests.

### BP-D23 — m65: add `e2e-queue.md` to the lint doc set + fix its 2 unresolved `GotoProbe` refs

- **Files:** `tools/lint/check-doc-refs.ps1` (:48-56); `docs/e2e-queue.md` (:157, :164).
- **Change:** (a) Add `'docs/e2e-queue.md'` to the `$Docs` array in `check-doc-refs.ps1` (the
  current set at :48-56 omits it). (b) The two backticked `GotoProbe` refs (:157, :164) are
  seeded scratch-solution file names, not repo symbols — add `'GotoProbe'` to the external
  allowlist (with a comment: "the seeded scratch-solution GotoProbe partial pair cited by the
  e2e-queue goto gates — not a repo symbol").
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` PASS (0 unresolved across the set
  INCLUDING `e2e-queue.md`).
- **Fails-if:** the lint reports the 2 `GotoProbe` refs as unresolved; or `e2e-queue.md` is
  still absent from the doc set.

### BP-D24 — m66: fix the `e2e-queue.md` "Status: EMPTY" header

- **Files:** `docs/e2e-queue.md` (:8-11).
- **Change:** The header "Status: EMPTY (2026-10-04). Every queued e2e gate has been run GREEN
  and removed from the active queue" contradicts the QUEUED gates at :40-68 (E2E-CR45-1..4 +
  E2E-CR34-1..4). Rewrite the header to state the actual status: the historical gates are
  discharged (see "What ran GREEN") but **8 gates are QUEUED** (E2E-CR45-1..4 + E2E-CR34-1..4,
  drained when their plans are GREEN).
- **Verify-with:** the header no longer says "EMPTY" and names the 8 queued gates; the doc-ref
  lint PASS.
- **Fails-if:** the header still claims EMPTY; or the queued-gate count in the header disagrees
  with the tables.

### BP-D25 — m67: annotate E2E-RC-1/2 + E2E-GOTO-1/2 as DISCHARGED

- **Files:** `docs/e2e-queue.md` (:130-167).
- **Change:** The "Queued gates — Telescope columns + preview" (:130-143) and "Queued gates —
  Gap 6 goto" (:145-167) sections still list E2E-RC-1/2 + E2E-GOTO-1/2 as queued with no
  DISCHARGED annotation, though both plans are GREEN (2026-10-04). Annotate each with the
  `~~strikethrough~~ — **DISCHARGED GREEN ...**` pattern used at :176-201, citing the
  discharging run (E2E-RC-1/2 → the columns+preview final gates, runs 169/170; E2E-GOTO-1/2 →
  the `telescope-goto` scenario, GREEN via the Gap-4 run 185 + the in-item 3rd-strike fix).
- **Verify-with:** each of the four gates carries a DISCHARGED annotation; the doc-ref lint PASS.
- **Fails-if:** any of E2E-RC-1/2/E2E-GOTO-1/2 still reads as QUEUED without a DISCHARGED note.

### BP-D26 — m68: refresh the `progress.md` header

- **Files:** `docs/progress.md` (:10-15).
- **Change:** The header "Updated: 2026-10-05 · Last item: Gap 4" is stale — the last completed
  item is the **34-findings code-review fixes (2026-10-06, GREEN in `4118e9d`)**. Update the
  date + last-item text to name the 34-findings plan (and note the 106-findings plan is the
  next pending item per the plan's queue position).
- **Verify-with:** the header names the 34-findings plan as the last item; the doc-ref lint PASS.
- **Fails-if:** the header still says "Last item: Gap 4" / "Updated: 2026-10-05".

### BP-D27 — m69: fix the baseline-count attribution

- **Files:** `docs/progress.md` (:257-258).
- **Change:** "297 passed; 212 passed (after Gap 4 — the recent-files finder, 2026-10-05)" —
  the counts are correct but the attribution is stale (they are post-34-findings). Change the
  parenthetical to "(after the 34-findings code-review fixes, 2026-10-06)".
- **Verify-with:** the parenthetical names the 34-findings plan; the doc-ref lint PASS.
- **Fails-if:** the "(after Gap 4)" attribution remains.

### BP-D28 — m70: fix the RUN ORDER "gap 6" pending entry

- **Files:** `docs/progress.md` (:409).
- **Change:** The RUN ORDER lists "**gap 5 (next)** → gap 6 → gap 10 → ..." — gap 6 (goto) is
  DONE (GREEN 2026-10-04). Strike it through like the others:
  "**gap 5 (next)** → ~~gap 6~~ (**DONE** GREEN 2026-10-04) → gap 10 → ...".
- **Verify-with:** gap 6 is struck through as DONE in the RUN ORDER; the doc-ref lint PASS.
- **Fails-if:** gap 6 still reads as pending.

### BP-D29 — m71: refresh `code-review.md` (the 77-vs-106 count discrepancy + the 12 unresolved lint refs)

- **Files:** `docs/reviews/code-review.md` (:11, :17-128, :226).
- **Change:** (a) The summary at :11 says "**77 findings: 0 critical, 12 major, 45 minor, 20 nit**"
  but the findings table lists **106 rows (12 M + 74 m + 20 n)**. Fix the summary to
  "**106 findings: 0 critical, 12 major, 74 minor, 20 nit**" (matching the table). Verify the
  counts at :225 (297/297, 212/212) are correct (they are — the 2026-10-06 refresh already
  updated them; the "34 findings as open" claim at :5 is already corrected to "fully fixed in
  `4118e9d`"). (b) **The doc-ref lint currently FAILS with 12 unresolved refs in this file**
  (verified 2026-10-06 at handoff — a pre-existing condition from the review refresh, NOT
  introduced by this plan): the backticked `.cs` (×8) + `.json` (×1) in the M8/m49 finding
  text (interpreted as file paths), `NoReflection` (the m55 finding's NEW test name),
  `GotoProbe` (the m65 finding's seeded scratch file), and `GetInterfaceMap` (the
  verified-clean VsVim interop). Fix: reword the backticked `.cs`/`.json`/`GetInterfaceMap`
  refs (remove the backticks or reword to prose — they are not file paths), and add
  `NoReflection` + `GotoProbe` to the `$intentionallyAbsent`/external allowlist in
  `tools/lint/check-doc-refs.ps1` (the established pattern — `GotoProbe` is also allowlisted
  by BP-D23 for `e2e-queue.md`). The doc-ref lint is the acceptance gate.
- **Verify-with:** the summary count matches the table (106 = 12+74+20); `pwsh tools/lint/check-doc-refs.ps1` PASS (0 unresolved — the 12 code-review.md refs are fixed).
- **Fails-if:** the summary still says 77 (or 45 minor / 20 nit mismatches the table); the doc-ref lint still reports the 12 code-review.md refs.

### BP-D30 — m72: fix the SKILL.md Down-tolerance line (coordinate with the Phase-1 M7 fix)

- **Files:** `.opencode/skills/vs-extension-dev/SKILL.md` (:240-241).
- **Change:** "DOWN uses a `> 1` pixel tolerance" is stale (the tolerance was removed in
  `349fc05`; Phase 1 M7 changes Down to `c.Y > a.Bottom`). Update to the post-M7 contract:
  "DOWN uses `c.Y > a.Bottom` (strictly beyond the active's bottom edge — no pixel tolerance;
  Up uses `c.Bottom < a.Y`)". This step runs after Phase 1, so the doc matches the fixed code.
- **Verify-with:** the line no longer mentions a `> 1` tolerance and states the post-M7
  `c.Y > a.Bottom` contract; the doc-ref lint PASS.
- **Fails-if:** the `> 1` tolerance wording remains; or the doc contradicts the post-M7 code.

### BP-D31 — m73: fix the SKILL.md `Panes/` parenthetical (the geometric resolver lives in `FocusTargetModel`)

- **Files:** `.opencode/skills/vs-extension-dev/SKILL.md` (:92).
- **Change:** The `Panes/` entry claims "GEOMETRIC directional Ctrl+H/J/K/L movement
  (LEFT/DOWN/UP/RIGHT — the collapsed single focus resolver, logged no-op edges)" — that
  resolver lives in `FocusTargetModel` (per :101). Trim the `Panes/` parenthetical to the pane
  host only (ordered registry, focused-pane tracking, left-click normalization) and point the
  geometric-resolver attribution at `FocusTargetModel`.
- **Verify-with:** the `Panes/` entry no longer claims the geometric resolver; the doc-ref lint PASS.
- **Fails-if:** the `Panes/` parenthetical still misattributes the geometric resolver.

### BP-D32 — m74: fix the two agent slice-B file lists

- **Files:** `.opencode/agent/neovim_review_hub.md` (:176-178); `.opencode/agent/code-review-hub.md` (:305-307).
- **Change:** Both slice-B lists reference the deleted `LinqExtensionMethods.cs` and omit four
  current files. **Evidence (verified):** `MyExtension/Navigation/` contains
  `WindowNavigator.cs`, `WindowNavigationEngine.cs`, `Utils/WindowFrameAdapter.cs`,
  `Utils/WindowFrameUtils.cs`, `Utils/NavigationConstants.cs`, `Utils/WindowRect.cs`,
  `Utils/NavigationSnapshot.cs`, `Utils/NavigationSettings.cs`, `Utils/Direction.cs` — and NO
  `LinqExtensionMethods.cs`. Replace `LinqExtensionMethods.cs` with the four omitted files:
  `WindowNavigationEngine.cs`, `Utils/NavigationSnapshot.cs`, `Utils/NavigationSettings.cs`,
  `Utils/Direction.cs` (both lists).
- **Verify-with:** grep both files for `LinqExtensionMethods` returns 0; the four current files
  are present in both slice-B lists; the doc-ref lint PASS.
- **Fails-if:** `LinqExtensionMethods.cs` remains in either list; or any of the four current
  files is still omitted.

### BP-D33 — n1: fix the stale `WindowNavigator.cs` comment line ref

- **Files:** `MyExtension/Navigation/WindowNavigator.cs` (:27).
- **Change:** The comment cites "InputHandler.cs:554" — the `new WindowNavigator(...)` call site
  has moved (now ~:583). Update the line ref to the current call site (verify with
  `rg -n "new WindowNavigator" MyExtension/Input/InputHandler.cs`).
- **Verify-with:** the comment's line ref matches the actual `new WindowNavigator` call site;
  the doc-ref lint PASS.
- **Fails-if:** the comment still cites :554 (or a ref that does not match the call site).

### BP-D34 — n10: DOCUMENT the DOC-66-3 `$baselineRightAttr` moving-target allowlist (no code change)

- **Files:** `tools/lint/check-doc-content.ps1` (:160).
- **Change:** DOC-66-3's `$baselineRightAttr` is a moving-target allowlist of ~11 prose patterns
  (the baseline "right" attribution wording drifts across doc refreshes). DOCUMENT the accepted
  design in a comment above it: the allowlist is deliberate — the assertion checks the baseline
  line's STRUCTURE (a `right`-attribution phrase), not an exact string, because the doc's
  wording legitimately varies; the ~11 patterns are the observed variants. No code change.
- **Verify-with:** the comment documents the accepted design; `pwsh tools/lint/check-doc-content.ps1`
  PASS (12/12).
- **Fails-if:** the allowlist is changed/removed without the documenting comment; or the
  doc-content lint fails.

### BP-D35 — n11: fix the `progress.md` dangling fragment

- **Files:** `docs/progress.md` (:129).
- **Change:** Line 129 "of the user-chosen **"smallest first"** run order (2026-10-03):" is a
  dangling fragment (the sentence it continues was cut; the run order is already described at
  :122-124). Delete the fragment line.
- **Verify-with:** the fragment is gone (the paragraph reads cleanly); the doc-ref lint PASS.
- **Fails-if:** the dangling fragment remains.

### BP-D36 — n12: fix the `progress.md` "In-progress" citation

- **Files:** `docs/progress.md` (:335).
- **Change:** "In-progress" says "(none — Gap 3 reached GREEN 2026-10-04 ...)" — the last
  completed item is the 34-findings plan (2026-10-06). Update to cite the 34-findings plan as
  the last completed item (and note the 106-findings plan is the next pending item).
- **Verify-with:** the In-progress section cites the 34-findings plan; the doc-ref lint PASS.
- **Fails-if:** the In-progress section still cites Gap 3 as the last completed item.

### BP-D37 — n20: fix the `NeoVisualLog.Log` comment (the debug duplication is opt-in)

- **Files:** `Telescope/Logging/NeoVisualLog.cs` (:109-112).
- **Change:** The comment claims "a line here lands in BOTH per-run files ... keeping them
  comparable" — but the `Debug.WriteLine` duplication is gated on `DebugDuplicationEnabled`
  (:114), so the debug file only receives the line when the opt-in flag is set. Fix the comment
  to state: the structured line always goes to the NeoVisual file (`LogFileWriter.Write`); the
  debug-output file receives it ONLY when `DebugDuplicationEnabled` is set (opt-in).
- **Verify-with:** the comment matches the `DebugDuplicationEnabled` gate; the doc-ref lint PASS.
- **Fails-if:** the comment still claims unconditional dual-file writes.

## Verification Trace

| failing test / scenario | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_FzfFilter_KillRegisteredBeforeWrite` (m51) | BP-D1 | no diagnostic — the marker-poll replaces `Thread.Sleep(200)`; passes deterministically |
| `Run_LogFileWriter_FlushTimer_OneShotFires` / `_IdleDoesNotFire` + `Run_LogFileWriter_FlushTimerRestored` (m52) | BP-D2 | no diagnostic — `FlushTimerIsReal` seam true after the restore |
| `Run_FocusTargetModel_DirectionTable` / `_IsSingleResolver` / `Run_PaneSelectionSync_StepsRemoved` / `Run_PaneHost_SingleOwner` / `Run_FileContentCache_Lru` / `_Shared` (m53) | BP-D3 | no diagnostic — seams replace the 7 reflection sites; grep `GetField|GetMethod` = 0 |
| `Run_WindowManager_DefaultControllerCache_ReturnsCachedInstance` + `Run_GetController_SameInstance` (m55) | BP-D4 | no diagnostic — both deleted; the hermetic `_NoReflection` test pins the contract |
| `Run_FocusGuard_NonToolWindowBlocks` / `_TextInputSurfaceFocused_*` (m56) | BP-D5 | no diagnostic — one assertion per case |
| `Run_BlockCaretState_DesiredVsRendered` (m57) | BP-D6 | `[NeoVisual] block-caret active=True|False` UNCHANGED; the `BlockCaretState` seam drives the test |
| m58 + n3 (TestRunner timeout risks) | BP-D7 | no diagnostic — the `TestRunner.cs:57-68` comment documents both risks |
| `Run_Assert_StackTrace` (n2) | BP-D8 | no diagnostic — `FormatFailure` output contains the stack trace |
| n4 (`PositiveActionKeyCount`) | BP-D9 | no diagnostic — constant deleted, build clean |
| `Run_KeyNames_RoundTrip_WindowPrefix_*` (n5) | BP-D10 | no diagnostic — three split tests pass |
| n6 + M11 (Down-tolerance comments) | BP-D11 | no diagnostic — comments match the post-M7 contract |
| `Run_Assert_Equal` (n7) | BP-D12 | no diagnostic — `Assert.Equal` prints actual/expected; grep `Assert.True(x==y)` = 0 |
| `Run_TestRunner_Timeout` (n8) | BP-D13 | no diagnostic — completes in ~6s, `FAIL Run_Sleeps` + `PASS Run_After` |
| `Run_LineIndex_LineOfMatchesNavigator` (n18) | BP-D14 | no diagnostic — KEPT + documented as the only cross-implementation check |
| `Run_IsKeyOfInterest_OverlayOpenShortCircuit` (m59) | BP-D15 | no diagnostic — the `SetOverlayOpenForTest` seam drives the test |
| the 8 `uiThreadDispatcher` tests (m61) | BP-D16 | no diagnostic — the `TestScaffold.SetCurrentDispatcherAsUiThread` seam; grep `uiThreadDispatcher` in Program.cs = 0 |
| `telescope-goto` re-walk trigger (M9) | BP-D17 | harness: the direct LogCache window scan finds `[Telescope] definitions gathered count=0` in `[idx, count)`; `-SelfCheck` PASS |
| `Run_FzfFilter_TimeoutRace` (M10) | BP-D18 | no diagnostic — the `DelayFactory` seam makes the boundary deterministic; `fzf filter failed: timeout after` ABSENT on the boundary-completion path |
| `telescope-preview` `tokens=` (m62) | BP-D19 | harness: the `preview tokens=\d+` assert is dropped; `preview file=` stays; `-SelfCheck` PASS |
| `neovisual-diagnostic-nav` severity outcomes (m63) | BP-D20 | harness: the four `diagnostic-nav direction=... severity=... target=...` asserts are UNCONDITIONAL; `-SelfCheck` PASS |
| `explorer-open-navigation` (m64) | BP-D21 | harness: `editor-view-opened` replaced by `Wait-ActiveDocumentMatch` on the selected file; `-SelfCheck` PASS |
| `iterate-telescope.ps1` sleeps (n9) | BP-D22 | harness: the two 1200ms sleeps replaced by bounded polls; script parses |
| doc-ref lint (m65) | BP-D23 | `pwsh tools/lint/check-doc-refs.ps1` PASS (0 unresolved incl. `e2e-queue.md`) |
| doc-content lint (m66-m74, n1, n10-n12, n20) | BP-D24..BP-D37 | `pwsh tools/lint/check-doc-refs.ps1` PASS + `pwsh tools/lint/check-doc-content.ps1` PASS (12/12) |

## Known-RED allowlist

- **NONE.** The baseline is all-GREEN (Telescope 297, NeoVisual 212, 44/44 e2e). No scenario or
  test in this section is a pre-existing known-RED; the e2e scenarios are DEFERRED to the queue
  (not run here), so no e2e regression can be misreported.

## Key decisions (do not second-guess)

1. **m53's absence guards** (SelectTarget/Steps/PaneNavigationEngine/_active) are replaced by
   public capability seams where feasible and DELETED (behavior tests retained) where the
   deletion is compile-time-enforced — the reflection absence check is not kept.
2. **M9's fix is the direct LogCache window scan** (the `Assert-NoEnterStorm` idiom), NOT the
   review's "fresh `Get-LogCacheIndex` snapshot" (which returns the cache end, past the
   0-gather line — verified `harness-common.ps1:96-101`).
3. **m62 drops** the `preview tokens=\d+` harness assert (the count reads 0 for both buffer
   sources — gating to non-zero is impossible); the source diagnostic is UNCHANGED.
4. **m52/m57/m59/m61 add production seams** (`FlushTimerIsReal`, `BlockCaretState`,
   `SetOverlayOpenForTest`, `TestScaffold.SetCurrentDispatcherAsUiThread`) — the only
   production source edits in this section; all are internal/test-only, no behavior change.
5. **m72's SKILL.md Down line** describes the POST-M7 contract (`c.Y > a.Bottom`) because
   Phase 1 runs before Phase 10 in the top-to-bottom pass.

## Plan corrections (with evidence)

- **M9's second fix option is wrong as literally stated:** "take a fresh `Get-LogCacheIndex`
  snapshot ... and reset `$script:LogSearchedTo` to it" — `Get-LogCacheIndex` returns
  `$script:LogCache.Count` (the cache END, `harness-common.ps1:96-101`), which is past the
  0-gather line; the correct variant is the direct window scan (or resetting the cursor to the
  ORIGINAL `$idx`). Baked into BP-D17.
- **m61's "restore only runs on a normal return"** is inaccurate for the current try/finally
  code (the restore DOES run on exception); the real risk is the m58 timeout abandoning the
  thread mid-mutation. The seam (BP-D16) + the m58 doc (BP-D7) cover both.
- **m71's "34 findings as open + stale counts 288/200"** is already fixed by the 2026-10-06
  refresh (the file now says 297/212 and "fully fixed in `4118e9d`"); the remaining fix is the
  summary's 77-vs-106 count discrepancy (BP-D29).

---

## Verification Trace (aggregated — AC → BP steps)

| Acceptance criterion | BP steps | failing test / scenario | expected diagnostic / proof |
|---|---|---|---|
| AC1 — query-driven finders hazard-free (M1/M2/M3/m2/m3/m4/m6/m7/m8/m9/m24/m25/m54) | Section A Phase 0 (BP-1..BP-11) | `Run_GrepFinder_GetCandidatesAsync_NoBlock`, `Run_FzfFinder_BatchedFilter`, `Run_FzfLineMapper_Boundary`, `Run_FileContentCache_IOOutsideLock`, `Run_FzfFinder_LiteralFallbackOffThread`, `Run_CodeIssuesFinder_TodoScanOffThread`, `Run_FileContentCache_SharedEntry`, `Run_FzfFilter_NoSpuriousTimeoutOnCancel`, `Run_FzfFilter_GraceDelayToken`, `Run_LiteralLineScanner_Direct` | `grep hits=`/`fzf hits=` byte-stable; the fzf hit ORDER may change (M2); the spurious `fzf filter failed: timeout` no longer fires (m8) |
| AC2 — navigation/interop fixed (M6/M7/M11/m17/m19/m21/m22/m47/m48/n13/n14/n15) | Section A Phase 1 (BP-12..BP-23) | `Run_WindowFrameUtils_LinkedWindowIsolation`, `Run_WindowNavigationEngine_Down_BelowBottom`, `Run_WindowNavigationEngine_Down_OnePixelGapAccepted` (REWRITTEN), `Run_WindowNavigationEngine_Down_ToleranceExcludes` (DELETED), `Run_RecentFilesGatherer_Dispose_RequiresUiThread`, `Run_FocusKeeper_TickCancelled`, `Run_ToolWindowTypeResolver_FindResultsNotTextInput`, `Run_SolutionExplorer_TryMoveCachedAcrossKeys` | `navigate direction=`/`navigate activated index=` byte-stable; the overlapping-window TARGET may change (M7); `toolwindow-move key=` byte-stable |
| AC3 — text-input stale-frame leak closed (M5) | Section B Phase 2 (BP-1..BP-2) | `Run_VimModeTracker_MainEditorFocusedEvent`, `Run_FocusGuard_TextInputSurfaceInvalidatedOnEditorFocus` | `text-motion key=` no longer fires for the main editor from a stale frame; `vim-mode=` unchanged |
| AC4 — geometric pipeline de-duplicated (M4/n17) | Section B Phase 3 (BP-3..BP-6) | the ~30 `Run_FocusTarget_*` + ~12 `Run_WindowNavigationEngine_*` stay GREEN + `Run_SharedGeometricEngine_Parameters` | `focus target=Input\|List\|Preview` + `focus no-op:` + `navigate direction=` UNCHANGED |
| AC5 — overlay/fzf nits cleaned (M12/m1/m5/m16/m20/m23/m39/m42/m43/m44/m45/m46/n16) | Section B Phase 4 (BP-7..BP-17) | `Run_TelescopeOverlay_IsOpenRace`, `Run_PreviewNavigator_SetTextGuarded`, `Run_ResultsLog_OnChange`, `Run_GlobalKeyboardHook_HcActionGate`, `Run_TelescopeController_DisposeClosesOverlay`, `Run_PaneFailureTracker_Interlocked`, `Run_PromptMotionRouter_SingleMapKey`, `Run_TextMotionDispatcher_InsertPlacements` (UPDATED), `Run_FocusTargetModel_ShouldFocus` | `results columns=`/`results count=` fire on change only (m5); `textinput-enter-input after caret=` reflects AfterCaret (m46); `focus target=Input` no longer spurious on EnterInsert (n16) |
| AC6 — forest/tool-window fixed (M8/m49/m50/m60) | Section C Phase 5 (BP-1..BP-4) | `Run_HierarchyForestBuilder_Unfiltered`, `Run_HierarchyResolver_SingleCsFilter`, `Run_SolutionExplorer_MapChildrenExpandsFolders`, `Run_KeybindingConfig_Merge` | `solution-explorer select file=` byte-stable |
| AC7 — vim/input nits cleaned (m10-m15/m18/m35-m38) | Section C Phase 6 (BP-5..BP-15) | `Run_GlobalKeyboardHook_IsKeyDownFirst`, `Run_InputHandler_NoPerKeySentinelRead`, `Run_VimBufferSubscriptions_NoLeak`, `Run_VimModeSource_OnBufferClosedUsesCachedBuffer`, `Run_KeybindingConfig_RejectsPhysicalModifierLeader` (+ the 2 UPDATED pinning tests), `Run_RoslynGatherers_ColumnClamped`, `Run_InputHandler_ShouldRouteToolWindowKey_SingleOverload`, `Run_InputHandler_ShiftGateUsesPureOverload`, `Run_LeaderSequenceMatcher_NoDeadAction`, `Run_InputHandler_SingleControllerResolution` | `vim-mode=`/`leader-binding executed:`/`toolwindow-move key=` byte-stable |
| AC8 — duplication/dead-code cleaned (m26-m34/m40/m41/n19) | Section C Phase 7 (BP-16..BP-28) | `Run_FinderColumns_GrepFzfShared`, `Run_FinderColumns_FilesRecentShared`, `Run_HitCap_SharedConstant`, `Run_ResultColumn_DeadMembersRemoved`, `Run_ErrorItemsWalker_SharedWalk`, `Run_TextMotionHelper_SanitizeUsesShared`, `Run_PaneSelectionSync_ShellRemoved`, `Run_DefinitionFinder_SharedBody` + the compile-RED deletions | all existing `[Telescope]`/`[NeoVisual]` lines UNCHANGED; doc-ref lint PASS (the m26/m41 doc propagation) |
| AC9 — test infra cleaned (m51-m61/n2-n8/n18) | Section D Phase 8 (BP-D1..BP-D16) | `Run_FzfFilter_KillRegisteredBeforeWrite` (rewritten), `Run_LogFileWriter_FlushTimerRestored`, `Run_Assert_StackTrace`, `Run_Assert_Equal`, `Run_TestRunner_Timeout` (shortened), the seam-driven rewrites (m53/m55/m57/m59/m61) | n/a (test-infra; the `block-caret active=` literal unchanged) |
| AC10 — harness gates hardened (M9/M10/m62/m63/m64/n9) | Section D Phase 9 (BP-D17..BP-D22) | the changed assertion patterns + `pwsh tools/harness/test-e2e.ps1 -SelfCheck` PASS | `diagnostic-nav direction=... severity=... target=...` asserted unconditionally (m63); `preview tokens=` dropped (m62) |
| AC11 — docs/lint refreshed (m65-m74/n1/n10-n12/n20) | Section D Phase 10 (BP-D23..BP-D37) | `pwsh tools/lint/check-doc-refs.ps1` PASS (0 unresolved incl. `e2e-queue.md`) + `pwsh tools/lint/check-doc-content.ps1` PASS (12/12) | n/a (doc/lint) |

**Final gate (unit-only):** `dotnet build` 0 errors; `dotnet run --project tests/Telescope.Tests` (297 + the new tests) all GREEN; `dotnet run --project tests/NeoVisual.Tests` (212 + the new tests) all GREEN; both lints PASS; the harness `-SelfCheck` PASS; `-List` 44. The e2e gates E2E-CR77-1..6 are QUEUED (deferred to a capable machine).
