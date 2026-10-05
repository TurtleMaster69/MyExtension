# Plan — Code review fixes (45 findings, incl. nits)

> **Lane: feature (unit-only, e2e DEFERRED).** Diagnostic changes → **M-M7** (the new
> literals are declared: C1's named vim-mode tokens, D9's re-pinned `boxText=`, C7's
> `window type probe failed`). The e2e scenarios are QUEUED in `e2e-queue.md` (status
> QUEUED) — created/executed on a capable machine after this plan is GREEN (the user's
> 2026-10-05 instruction: "we are not on e2e capable machine so they should be put into
> queue"). RED is proven at the unit level only.
>
> **Source:** `docs/reviews/code-review.md` (2026-10-05, 45 findings: 0 critical, 9 major,
> 24 minor, 12 nit). The review's "Nothing filed yet — pending the user's Step-4 selection"
> is answered by the user's request: **ALL findings incl. nits**.
>
> **Research (4 subagents, 2026-10-05):** `trailmark-recon` structural digest (graph 2508
> nodes / 982 proxies; the pane-host chain `OnPreviewKeyDown→RouteInputKey→MapKey→Handle→Move→SelectTarget`;
> the fzf/overlay blast radius; 5 parser blind spots — `FzfFilter.FilterAsync`,
> `PreviewEditorHost.Show`, `SolutionExplorerController.TryMove`,
> `IsFocusedTextBoxInCurrentToolWindow`, `InjectedKeyGuard.TryConsume` — NONE dead, LSP-proven);
> `arch-auditor` ×2 change-area (Telescope/ + MyExtension/&tools/ — fix-direction verdicts +
> corrections below); `feature-researcher` native-VS-reuse (D8 fzf `--listen` viability, A1
> native controller mechanism, C1 VsVim mode contract, T1 outcome diagnostics).
>
> **Key research corrections baked in:**
> - **D1/D2 tension resolved:** you cannot BOTH share `PaneNavigationEngine` with
>   `WindowNavigationEngine` AND collapse it — the plan COLLAPSES (D2's direction), deleting
>   the mirrored engine (D1's duplication) into ONE pure focus resolver, preserving the
>   pinned tie-break (Ctrl+K Input→Preview — the equal-width last-in-list net) + the no-op
>   edges (no wrap) that `telescope-focus-panes` + ~20 unit tests pin.
> - **D5 is NOT a blanket `Task.Run`:** `GrepFinder.GetCandidates` calls
>   `ThreadHelper.ThrowIfNotOnUIThread()` — DTE enumeration must stay on the UI thread. Fix:
>   enumerate (cached via `ProjectFileCache`) on the UI thread, `Task.Run` ONLY the pure
>   `ScanFile` loop, marshal back with the existing generation check.
> - **D7 is half-right:** `ProjectFileCache` is ALREADY shared (injected in all three ctors);
>   only `FileContentCache(500)` is per-instance (3×500-entry caches) — inject ONE shared
>   `FileContentCache` from `TelescopeController`.
> - **D8:** `QuoteArg` is a CORRECT Windows argv-quoting implementation (not the problem); the
>   real cost is the per-keystroke spawn (already off-thread, e2e-GREEN). Fix: unit-test
>   `QuoteArg` + document the keep-subprocess decision; `--listen`/in-process deferred until
>   measurement proves a bottleneck (perf-investigation).
> - **D11:** `volatile` is ILLEGAL on `bool?` — use a separate `volatile bool _probed` +
>   `bool _value` (or lock/Interlocked).
> - **D15:** `Task.Delay` returns a Task, NOT IDisposable — "dispose" is wrong; the delay
>   already shares the caller's token (cancelled on overlay close). Fix: a dedicated CTS
>   cancelled on completion (or document the harmless pending timer).
> - **A1:** NO native VS per-tool-window-type keyboard-controller mechanism exists
>   (`ToolWindowPane`/`IVsToolWindowFactory` only create windows; VS routing is command-based).
>   Fix: delete the eager registration loop, keep the lazy `_defaultControllers` cache (the
>   designed R20 mechanism; `ResolveController` depends on it — the m22 same-instance invariant).
> - **A3:** DTE `ErrorItems` exposes NO version counter — a count-keyed cache is weak (same
>   count, different items after a build). Fix: a short TTL cache or a bounded scan; the cache
>   decision lives in a pure helper.
> - **C1:** VsVim `ModeKind` has more modes than the three. Fix: EXTEND the contract — name the
>   common extra modes (Visual, Command, VisualBlock, Select) + document `vim-mode=Unknown`
>   (focus loss) + the numeric fallback. M-M7.
> - **C5:** do NOT broaden `IsEditorFocused` (it would track the Telescope preview view —
>   deliberately non-Editable — and flip the flag while the overlay preview is focused) —
>   DOCUMENT the fail-open risk instead.
> - **T1:** assert `navigate activated index=\d+` per chord, NOT "no no-op at all" (some
>   directions may legitimately no-op depending on the scratch layout).
> - **T3:** `Assert-NoSeedLeak` is at `test-e2e.ps1:551` (NOT harness-common.ps1 — the review's
>   file attribution was off by one file).
> - **T4:** a persistent `$searchedTo` cursor must PRESERVE the fixed-baseline contract
>   (search after baseline without advancing on match — AGENTS.md forbids an advancing cursor).

## Goal

Fix ALL 45 code-review findings (incl. nits) from `docs/reviews/code-review.md` (2026-10-05):
the modular telescope architecture's sharp edges (the over-engineered pane host, the fzf
kill-on-cancel/race/timer hazards, the overlay clamp/UI-freeze/legacy-diagnostic issues, the
per-finder cache fragmentation), the tool-window/navigation dead-code + perf hazards, the
harness false-positive gates, and the stale docs — with every fix RED-proven at the unit
level and the e2e scenarios QUEUED (deferred to a capable machine).

## Approach (phases)

**Phase 0 — Pane-host consolidation (D1, D2, D6, D14).** Collapse `FocusTargetModel` +
`PaneNavigationEngine` into ONE small pure focus resolver (`FocusTargetModel`), DELETE the
mirrored `PaneNavigationEngine` + `PaneRect`/`PaneDirection`/`PaneAxis`/`GapTo`/`Adjacency`/
`IsInDirection`/`IsAligned` (D1's duplication gone), keep `IPane`/`PaneHost` as the reusable
contract (D2), make `PaneHost` derive the previously-active pane from the model instead of
storing `_active` (D6 — single owner), and single-source the Ctrl-chord mapping
(`PaneFocusKey` + `MapKey` + `OverlayKeyHandler`'s CtrlH/CtrlL — one chord→direction map,
D14). The focus BEHAVIOR is byte-identical: `focus target=` / `focus no-op:` literals
unchanged; the pinned tie-break + no-op edges preserved. The existing ~20
FocusTargetModel/PaneNavigationEngine unit tests are the RED harness (they pin the behavior).

**Phase 1 — fzf filter hardening (D3, D8, D11, D15).** Register the kill callback on the
token BEFORE the spawn/write (D3); split `_availability` into `volatile bool _probed` +
`bool _value` (D11); unit-test `QuoteArg` + document the keep-subprocess decision (D8);
cancel the timeout timer via a dedicated CTS on completion (D15). `FzfFilter` is hermetic —
the tests are the RED harness.

**Phase 2 — Overlay correctness (D4, D5, D9, D10, D12).** Clamp `ApplyPreviewCaret` via
`PreviewCaretMap.Offset` like `ShowPreview` (D4); move the query-driven gather's pure
`ScanFile` loop to a background task, enumerate on the UI thread, marshal back with the
generation check (D5); re-pin `RenderedTextLength`/`boxText=` to a meaningful value (D9 —
M-M7); clear `_gPending` in `TryPromptMotion` via a new `OverlayKeyHandler.CancelPendingG()`
(D10 — `g h g` must NOT trigger `gg`); `ResultMapper` byDisplay → `Ordinal` (D12).

**Phase 3 — Finder cache sharing (D7).** Inject ONE shared `FileContentCache` from
`TelescopeController` (the `ProjectFileCache` is already shared).

**Phase 4 — WindowManager + navigation (A1, A6, A7, C7).** Delete the eager controller
registration loop, keep the lazy `_defaultControllers` cache (A1); `BuildActiveWindows`
returns a COPY of `_cachedLinked` (A6 — kills aliasing, preserves the cache); `IsTextInputType`
derives from a single classification source (A7); handle/log the `GetGuidProperty` HRESULT
(C7).

**Phase 5 — Preview + Error List perf (A2, A3).** Cache the preview text keyed on
`ITextSnapshot.Version.VersionNumber` (A2 — extract a pure version→text cache helper); a
short-TTL/bounded Error List scan with the cache decision in a pure helper (A3).

**Phase 6 — Tool-window controllers (A4, A5, A8, A9, A10, A11, C2, C3, C6).** `TryMove` delegates to
`ToolWindowControllerBase.TextMotion` preserving the N21 side effect + the tree h/l guard
(A4); correct the `TextMotionHelper` WPF comment / read a bounded window (A5); extract the
`HandleKey` tool-window routing block (A8); `_sessionMru` → LinkedList or cap (A9); dedupe
the DocView walk-up loop (A10); single `CurrentController` resolution per key-down (A11);
`FocusKeeper` per-controller or owner-check (C2); cache/bound the box-walk (C3); reset
`_focusKeeper` to null after dispose (C6).

**Phase 7 — Vim mode contract (C1, C5).** Extend `VimModeClassifier`'s name table to the
common extra modes (Visual, Command, VisualBlock, Select) + document `vim-mode=Unknown`
(focus loss) + the numeric fallback in AGENTS.md (C1 — M-M7); DOCUMENT the `IsEditorFocused`
fail-open risk rather than broadening it (C5).

**Phase 8 — InjectedKeyGuard (C4).** Document the accepted risk (VK+TTL-only matching; a
dropped injected event could consume the next physical same-VK within 1s — theoretical;
`keybd_event` queues synchronously). No behavior change.

**Phase 9 — Harness (T1, T3, T4, T5, T6).** `neovisual-window-nav` asserts the navigation
OUTCOME — `navigate activated index=\d+` per chord (T1); `Assert-NoSeedLeak` throws in a
full (non-reuse) run when the expected tree is absent (T3); `Wait-LogLine`'s `$searchedTo`
becomes a persistent cursor preserving the fixed-baseline contract (T4); `telescope-navigate`
uses the LogCache tail-read (T5); the runner enforces the suite-order invariants (T6).

**Phase 10 — Pane contract tests (T2).** `IPane`-contract tests with a fake pane (the
concrete panes are thin WPF shells; the testable logic is the contract + focus model).

**Phase 11 — Docs (DOC1-DOC6).** Refresh the stale docs: AGENTS.md's "43 executed GREEN /
telescope-recent pending" → 44/44 GREEN (DOC1); spec.md's 43-vs-44 scenario list (DOC2);
progress.md's pending-queue run-order block (DOC3); the prior-review "still open" annotations
N54/N55/W12 → resolved (DOC4); code-review.md's stale test counts 153/163 → 268/191 (DOC5);
spec.md §7's missing git-bindings bullet (DOC6).

## Acceptance criteria

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | The pane host is consolidated (D1/D2/D6/D14): ONE pure focus resolver, single owner, single chord map; the focus behavior byte-identical | `focus target=Input|List|Preview` + `focus no-op:` UNCHANGED | the existing ~20 FocusTargetModel/PaneNavEngine tests stay GREEN + new single-owner/chord-map tests; e2e `telescope-focus-panes` |
| AC2 | fzf filter hardened (D3/D8/D11/D15): kill-on-cancel before the write, no cross-thread `_availability` race, tested `QuoteArg`, no leaked timeout timer | `fzf hits=...` / `fzf filter failed:` UNCHANGED | `FzfFilter` hermetic tests (RED for the new behavior) |
| AC3 | Overlay correctness (D4/D5/D9/D10/D12): clamped caret, background scan, meaningful `boxText`, no stale `_gPending`, Ordinal mapping | `preview caret=...` unchanged; `results count=... boxText=<re-pinned>` (M-M7); `prompt-motion key=...` unchanged | PreviewCaretMap / ScanFile / RenderedTextLength / OverlayKeyHandler / ResultMapper tests |
| AC4 | Finder caches shared (D7) | `grep hits=...` / `fzf hits=...` UNCHANGED | the shared-`FileContentCache` test |
| AC5 | WindowManager/navigation cleaned (A1/A6/A7/C7): one controller mechanism, no aliased list, derived `IsTextInputType`, logged `GetGuidProperty` | `navigate direction=...` / `toolwindow-move key=...` UNCHANGED | NeoVisual.Tests: GetController-same-instance + copy + classification tests |
| AC6 | Preview + Error List perf (A2/A3): cached preview text, bounded Error List scan | `preview file=...` / `diagnostic-nav direction=...` UNCHANGED | the pure version→text cache + cache-decision helpers |
| AC7 | Tool-window controllers cleaned (A4/A5/A8/A9/C2/C3/C6) | `solution-explorer ...` / `text-motion key=...` UNCHANGED | the extracted-block / MRU / FocusKeeper tests |
| AC8 | Vim-mode contract aligned (C1/C5) | `vim-mode=Insert|Normal|Replace|Visual|Command|VisualBlock|Select` (extended, M-M7) + `vim-mode=Unknown` documented | VimModeState / VimModeClassifier tests |
| AC9 | InjectedKeyGuard risk documented (C4) | UNCHANGED | the existing guard tests + the documented-risk comment |
| AC10 | Harness gates hardened (T1/T3/T4/T5/T6) | `navigate activated index=\d+` asserted | e2e `neovisual-window-nav` + the full-suite re-run |
| AC11 | Pane contract tested (T2) | UNCHANGED | `IPane` fake-pane tests |
| AC12 | Docs refreshed (DOC1-DOC6) | n/a | doc-ref + doc-content lints PASS |

## Unit test plan

- **`tests/Telescope.Tests`** (the hermetic seams): the pane-host consolidation (the existing
  FocusTargetModel/PaneNavigationEngine tests stay GREEN + new single-owner/chord-map tests);
  `FzfFilter` (kill-on-cancel ordering, `_probed`/`_value` split, `QuoteArg` edge cases, the
  timeout-CTS); `PreviewCaretMap` clamp; the `ScanFile` background loop; `RenderedTextLength`
  re-pin; `OverlayKeyHandler.CancelPendingG` (RED: `g h g` must NOT fire `gg`); `ResultMapper`
  Ordinal; the shared-`FileContentCache`; the `IPane` fake-pane contract tests.
- **`tests/NeoVisual.Tests`** (the hermetic seams): `GetController`/`ResolveController`/
  `DefaultControllerFor` same-instance (RED if the lazy cache is deleted); `BuildActiveWindows`
  returns a copy; `IsTextInputType` classification; the version→text cache helper; the
  Error-List cache-decision helper; the MRU helper; `FocusKeeperSchedule` owner-check;
  `VimModeState`/`VimModeClassifier` (focus-loss emits only documented tokens; the extra modes
  named); the `InjectedKeyGuard` documented-risk comment.
- **RED proof:** every new test fails WITHOUT the fix and passes WITH it (the
  verify-tests-fail-without-fix discipline). The pane-host collapse's RED is the existing
  suite (a behavior change breaks the pinned tests).

## Diagnostics (M-M7 — the new literals)

- **C1 (NEW named tokens):** `vim-mode=Visual|Command|VisualBlock|Select` replace the numeric
  `vim-mode=<n>` for the common extra VsVim modes; `vim-mode=Unknown` (focus loss) is
  DOCUMENTED in AGENTS.md as a legitimate token. The existing `vim-mode=Insert|Normal|Replace`
  contract is unchanged.
- **D9 (re-pinned value):** `results count=... boxText=<meaningful value>` — the value
  changes from the legacy dead-layout math to a real rendered length; the harness regexes
  (`results count=\d+ selected=...`) do NOT pin `boxText`, so no harness break.
- **C7 (NEW literal, BP-18):** `[NeoVisual] window type probe failed: {msg}` — logged when the
  `GetGuidProperty` HRESULT fails (the n19 `window rect unavailable` precedent); the failure
  is observable instead of a silent `_type = Unknown`. Presence-only — no harness break.
- All other findings are diagnostic-neutral (the `[Telescope]`/`[NeoVisual]` literals
  byte-stable).

## Known-RED allowlist

- **NONE.** The baseline is all-GREEN (44/44 e2e, Telescope 268, NeoVisual 191). The
  `telescope-goto` flaky ledger is CLOSED (BP-B7 fixed it — GREEN outright). Per-scenario
  flaky counts start at 0.

## E2E queue reference (e2e DEFERRED — queued in `e2e-queue.md`, status QUEUED)

> The user's 2026-10-05 instruction: "we are not on e2e capable machine so they should be put
> into queue." The four scenarios below are QUEUED (never created/executed now); they become
> READY when this plan is GREEN and are created/executed on a capable machine.

- **E2E-CR45-1** — `neovisual-window-nav` UPDATED to assert the navigation OUTCOME
  (`navigate activated index=\d+` per chord) — T1.
- **E2E-CR45-2** — `telescope-focus-panes` stays GREEN (the pane-host consolidation must not
  change the focus behavior) — D1/D2/D6/D14.
- **E2E-CR45-3** — `neovisual-editor-insert` + a named-mode assertion (`vim-mode=Visual` or
  the documented `Unknown` on focus loss) — C1.
- **E2E-CR45-4** — the full 44-scenario fresh-boot suite re-run (covers T3/T4/T5/T6 + the
  fzf/overlay/finder/tool-window/navigation changes — D3/D4/D5/D7/D8/D9/D10/D11/D12/D15 +
  A1-A9 + C2-C7) — the regression gate.

## Files to be touched

- **Telescope/:** `Overlay/Utils/Panes/PaneNavigationEngine.cs` (DELETE), `Overlay/Utils/FocusTargetModel.cs`
  (collapse), `Overlay/Utils/Panes/PaneHost.cs` (single owner), `Overlay/Utils/OverlayKeyHandler.cs`
  (CancelPendingG + the chord map), `Filter/FzfFilter.cs`, `Overlay/TelescopeOverlay.cs`,
  `Overlay/Utils/ResultsFormatter.cs`, `Overlay/Utils/ResultMapper.cs`, `Finders/GrepFinder.cs`,
  `Finders/FzfFinder.cs`, `Finders/CodeIssuesFinder.cs`, `Controller/TelescopeController.cs`.
- **MyExtension/:** `Package/MyExtensionPackage.cs`, `ToolWindows/WindowManager.cs`,
  `ToolWindows/GeneralToolWindowController.cs`, `ToolWindows/SolutionExplorerController.cs`,
  `ToolWindows/Utils/TextMotionHelper.cs`, `ToolWindows/Utils/FocusKeeper.cs`,
  `Input/InputHandler.cs`, `Package/Utils/RecentFilesGatherer.cs`, `Package/Utils/PreviewEditorHost.cs`,
  `Package/Utils/ErrorListGatherer.cs`, `Navigation/WindowNavigator.cs`, `Vim/VimModeTracker.cs`,
  `Vim/Utils/VimModeClassifier.cs`, `Hooks/Utils/InjectedKeyGuard.cs`.
- **tests/:** `tests/Telescope.Tests/Program.cs`, `tests/NeoVisual.Tests/Program.cs`.
- **tools/:** `tools/harness/test-e2e.ps1`, `tools/harness/harness-common.ps1`.
- **docs/:** `AGENTS.md`, `docs/spec.md`, `docs/progress.md`, `docs/reviews/code-review.md`,
  `docs/reviews/architecture-review.md`.

## Open risks

1. **The pane-host collapse (D1/D2/D6/D14) is the highest-risk change.** The pinned tie-break
   (Ctrl+K Input→Preview — the equal-width last-in-list net) + the no-op edges must survive
   byte-identically. The ~20 unit tests + `telescope-focus-panes` are the guard; if the
   collapse changes the tie-break, the tests/e2e catch it (RED). Do NOT "simplify" the
   tie-break — it is pinned.
2. **D5's UI-thread affinity.** A blanket `Task.Run` around `GetCandidates` violates
   `ThrowIfNotOnUIThread()` — only the pure `ScanFile` loop moves off-thread. The overlay
   freeze fix must not introduce a DTE-on-background-thread bug.
3. **A3's cache staleness.** DTE `ErrorItems` has no version counter — a stale cache silently
   mis-navigates. The short-TTL/bounded-scan design must be conservative.
4. **C1's M-M7 churn.** The extended `vim-mode=` tokens touch the diagnostic contract — the
   harness's `vim-mode=Insert|Normal|Replace` regexes stay valid (the three named tokens are
   unchanged), but any strict/negative assertion over the mode line must be re-checked.
5. **T4's fixed-baseline contract.** The persistent `$searchedTo` cursor must NOT advance on
   match (AGENTS.md forbids it) — the harness's per-scenario baseline semantics must hold.
6. **The queue position.** The code-review-fixes plan becomes the FIRST pending item at
   handoff (before Gap 5) — the counts are re-reads at the gate.

## Build Plan

> (Appended by `implementation-planner` — 2026-10-05. NO RED evidence exists yet — the unit
> tests are not written; they will be written by `neovim_hub`'s `e2e-test-builder` in the
> unit-only lane. Verify-with = unit test NAMES + diagnostic FORMATS only. The e2e scenarios
> (E2E-CR45-1..4) are DEFERRED to the e2e queue (status QUEUED) — created/executed on a
> capable machine after this plan is GREEN; the BP steps' e2e Verify-with references are the
> QUEUED gates, and the unit tests are the primary Verify-with. All 45 findings map
> to ≥1 BP-n step (coverage table below). The pinned research corrections are baked in — do
> NOT re-derive or contradict them.)

### Phase 0 — Pane-host consolidation (D1, D2, D6, D13, D14)

**BP-1 — Collapse `FocusTargetModel` + `PaneNavigationEngine` into ONE pure focus resolver; DELETE the mirrored engine (D1, D2)**
- **Files:** `Telescope/Overlay/Utils/FocusTargetModel.cs` (collapse — absorb the geometric decision), `Telescope/Overlay/Utils/Panes/PaneNavigationEngine.cs` (DELETE, incl. the `PaneRect`/`PaneDirection`/`PaneAxis` types at :7-75 + the private `GapTo`/`Adjacency`/`IsInDirection`/`IsAligned` methods at :181-203), `Telescope/Overlay/Utils/Panes/PaneHost.cs` (keep `IPane`/`PaneHost` as the reusable contract — D2).
- **Change:** Move the geometric selection pipeline (in-direction → aligned → closest gap → largest adjacency → last-in-list tie-break) INTO `FocusTargetModel.Handle(PaneFocusKey)` as a private pure method; delete the mirrored engine + its rect/direction/axis types. The pinned tie-break (Ctrl+K Input→Preview — the equal-width last-in-list net) and the no-op edges (no wrap) survive **byte-identically**; `focus target=` / `focus no-op:` literals unchanged. **Doc propagation (rule 5a):** grep docs for `PaneNavigationEngine`/`PaneRect`/`PaneDirection`/`PaneAxis` — AGENTS.md:96,174; docs/spec.md:116,117,203,351,352; docs/progress.md:420,892,893,934; `.opencode/skills/vs-extension-dev/SKILL.md`:93,102,103,150 — rewrite the descriptive prose to the collapsed `FocusTargetModel` (the doc-ref lint is the acceptance gate).
- **Verify-with:** the existing ~23 `Run_FocusTarget_*` tests (tests/Telescope.Tests/Program.cs:4000-4179) stay GREEN — `dotnet run --project tests/Telescope.Tests -- FocusTarget`; the 7 `Run_PaneNavEngine_*` tests (Program.cs:4192-4281) migrate to `Run_FocusTarget_*` equivalents (the pinned `Run_PaneNavEngine_AdjacencyTieGoesToLastInList` → `Run_FocusTarget_AdjacencyTieGoesToLastInList`); new test `Run_FocusTargetModel_IsSingleResolver` (compile-RED if `PaneNavigationEngine` is still referenced); e2e E2E-CR45-2 `telescope-focus-panes` stays GREEN; doc-ref lint `pwsh tools/lint/check-doc-refs.ps1` PASS.
- **Fails-if:** any `Run_FocusTarget_*`/`Run_PaneNavEngine_*` test fails; `[Telescope] focus target=` / `[Telescope] focus no-op:` literals change; `telescope-focus-panes` RED; doc-ref lint flags a stale `PaneNavigationEngine` ref.

**BP-2 — `PaneHost` becomes the single owner of the active pane (D6)**
- **Files:** `Telescope/Overlay/Utils/Panes/PaneHost.cs:19` (delete `_active`), `Telescope/Overlay/TelescopeOverlay.cs` (call sites that kept `_active` in sync).
- **Change:** `PaneHost` derives the previously-active pane from `FocusTargetModel.Current` instead of storing `_active` — the model is the single owner of focused-pane state; a desync (`focus target=X` while pane Y holds focus) becomes impossible.
- **Verify-with:** new test `Run_PaneHost_SingleOwner` (PaneHost activation state derives from the model; no `_active` field); the existing `Run_FocusTarget_*` tests stay GREEN; e2e E2E-CR45-2.
- **Fails-if:** `[Telescope] focus target=X` while pane Y holds focus; `telescope-focus-panes` RED.

**BP-3 — Single chord→direction map (D14)**
- **Files:** `Telescope/Overlay/Utils/FocusTargetModel.cs:29-37,129-140` (`PaneFocusKey` + `MapKey`), `Telescope/Overlay/Utils/OverlayKeyHandler.cs` (CtrlH/CtrlL cases).
- **Change:** single-source the Ctrl+H/J/K/L chord→direction mapping — `MapKey` and `OverlayKeyHandler`'s CtrlH/CtrlL both resolve through ONE map (e.g. `FocusTargetModel.MapKey(Key)` or a shared static table).
- **Verify-with:** new test `Run_ChordMap_SingleSource` (MapKey and the OverlayKeyHandler Ctrl-chord path agree for all four chords); existing `Run_FocusTarget_*` tests stay GREEN.
- **Fails-if:** Ctrl+H/L in the overlay diverges from `MapKey`; a chord maps to the wrong direction.

**BP-4 — Delete the `TryDispatch.cs` stub (D13)**
- **Files:** `Telescope/Overlay/Utils/TryDispatch.cs` (DELETE), docs/spec.md:119 + `.opencode/skills/vs-extension-dev/SKILL.md`:105 (remove the file row).
- **Change:** delete the comment-only stub retained after the merge into `TextMotionDispatcher`; remove its doc rows (rule 5a — grep docs for `TryDispatch`).
- **Verify-with:** `dotnet build` 0 errors; `dotnet run --project tests/Telescope.Tests -- TextMotionDispatcher` stays GREEN; doc-ref lint PASS (no stale `TryDispatch.cs` ref).
- **Fails-if:** doc-ref lint flags a stale `TryDispatch.cs` ref; a `Run_TextMotionDispatcher_*` test fails.

### Phase 1 — fzf filter hardening (D3, D8, D11, D15)

**BP-5 — Register the kill callback BEFORE the spawn/write (D3)**
- **Files:** `Telescope/Filter/FzfFilter.cs:153-177`.
- **Change:** move `cancellationToken.Register(() => TryKill(p))` to BEFORE the `Task.Run` spawn/write (or wrap the write in the same `using` scope as the registration) so cancellation can kill fzf during the blocking stdin write; the `using var p` scope must cover the registration.
- **Verify-with:** new test `Run_FzfFilter_KillRegisteredBeforeWrite` (a hung process that blocks on stdin write; cancel during the write → `TryKill` invoked and the filter returns promptly); existing `Run_FzfFilter_*` tests stay GREEN.
- **Fails-if:** a hung fzf leaks a process / strands `FilterAndUpdateAsync`; `fzf filter failed:` never appears on cancel.

**BP-6 — Split `_availability` into `volatile bool _probed` + `bool _value` (D11)**
- **Files:** `Telescope/Filter/FzfFilter.cs:77-85`.
- **Change:** `volatile` is ILLEGAL on `bool?` — replace `bool? _availability` with `volatile bool _probed` + `bool _value` (or lock/Interlocked). The probe runs once; the UI-thread read never races the thread-pool continuation.
- **Verify-with:** new test `Run_FzfFilter_AvailabilityProbeOnce` (probe cached once; a second `IsAvailableAsync` returns the cached value without re-spawning); existing `Run_FzfFilter_*` tests stay GREEN.
- **Fails-if:** fzf spawns twice after the probe cached false; a stale `null` re-probes.

**BP-7 — Unit-test `QuoteArg` + document the keep-subprocess decision (D8)**
- **Files:** `Telescope/Filter/FzfFilter.cs:250-277` + the class doc (:118-226).
- **Change:** `QuoteArg` is a CORRECT Windows argv quoter — add unit tests pinning the edge cases and document the keep-subprocess decision (the per-keystroke spawn is already off-thread and e2e-GREEN; `--listen`/in-process deferred until measurement proves a bottleneck — perf-investigation).
- **Verify-with:** new tests `Run_QuoteArg_Empty`, `Run_QuoteArg_Spaces`, `Run_QuoteArg_EmbeddedQuote`, `Run_QuoteArg_BackslashRun`, `Run_QuoteArg_TrailingBackslash` (Telescope.Tests).
- **Fails-if:** a `Run_QuoteArg_*` test fails; the keep-subprocess decision is not documented.

**BP-8 — Cancel the timeout timer via a dedicated CTS (D15)**
- **Files:** `Telescope/Filter/FzfFilter.cs:180`.
- **Change:** `Task.Delay` returns a Task (NOT IDisposable) and shares the caller's token — add a dedicated CTS cancelled on completion (or document the harmless pending timer). The fast path must not leave a per-keystroke pending timer.
- **Verify-with:** new test `Run_FzfFilter_TimeoutTimerCancelled` (the fast path cancels the dedicated CTS; no pending timer after a normal filter).
- **Fails-if:** a pending timer survives the fast path; `Task.Delay` is "disposed" (compile error).

### Phase 2 — Overlay correctness (D4, D5, D9, D10, D12)

**BP-9 — Clamp `ApplyPreviewCaret` via `PreviewCaretMap.Offset` (D4)**
- **Files:** `Telescope/Overlay/TelescopeOverlay.cs:1119-1122`.
- **Change:** clamp `_previewNavigator.Caret` through `PreviewCaretMap.Offset` against the current editor text, exactly as `ShowPreview` does (:880), so a stale buffer can never hand an out-of-range index to `SnapshotPoint`.
- **Verify-with:** new test `Run_PreviewCaretMap_Clamp` (an out-of-range caret clamps to the buffer length); existing `Run_PreviewCaretMap_*` tests stay GREEN; e2e `telescope-preview-motions` stays GREEN.
- **Fails-if:** an exception thrown from `OnPreviewKeyDown` on a stale buffer; `[Telescope] preview caret=... line=...` changes.

**BP-10 — Move the query-driven gather's pure `ScanFile` loop off the UI thread (D5)**
- **Files:** `Telescope/Overlay/TelescopeOverlay.cs:504-516`, `Telescope/Finders/GrepFinder.cs:107-110`, `Telescope/Finders/FzfFinder.cs`.
- **Change:** NOT a blanket `Task.Run` — `GrepFinder.GetCandidates` calls `ThreadHelper.ThrowIfNotOnUIThread()`; enumerate (cached via `ProjectFileCache`) on the UI thread, `Task.Run` ONLY the pure `ScanFile` loop, marshal back with the existing generation check (TelescopeOverlay.cs:506,512).
- **Verify-with:** new test `Run_GrepFinder_ScanFileBackground` (the pure `ScanFile` loop is off-thread; the DTE enumeration stays on the UI thread; the generation check discards stale results); existing `Run_GrepFinder_*` tests stay GREEN; e2e `telescope-grep`/`telescope-fzf` stay GREEN.
- **Fails-if:** the overlay freezes on a large solution; DTE is touched on a background thread (ThrowIfNotOnUIThread violation); `grep hits=...`/`fzf hits=...` change.

**BP-11 — Re-pin `RenderedTextLength`/`boxText=` to a meaningful value (D9 — M-M7)**
- **Files:** `Telescope/Overlay/Utils/ResultsFormatter.cs:24-39`.
- **Change:** `RenderedTextLength` exists solely to keep a legacy diagnostic byte-identical to the RETIRED TextBox render — re-pin it to a real rendered length. The harness regexes (`results count=\d+ selected=...`) do NOT pin `boxText`, so no harness break.
- **Verify-with:** new test `Run_ResultsFormatter_RenderedTextLength` (the value is a meaningful rendered length, not the legacy dead-layout math); the harness `results count=\d+ selected=...` regexes still pass.
- **Fails-if:** `results count=... boxText=` emits the legacy dead-layout value; the `results count=` line breaks.

**BP-12 — Clear `_gPending` in `TryPromptMotion` via `OverlayKeyHandler.CancelPendingG()` (D10)**
- **Files:** `Telescope/Overlay/Utils/OverlayKeyHandler.cs:156-164` (new `CancelPendingG()`), `Telescope/Overlay/TelescopeOverlay.cs:1031` (`TryPromptMotion` calls it).
- **Change:** `_gPending` is private — add `OverlayKeyHandler.CancelPendingG()` and call it from `TryPromptMotion` so a prompt motion (h/l/w/b/e/0/$) consumed before `_keyHandler.Handle` clears the pending `g` — `g h g` must NOT trigger `gg`.
- **Verify-with:** new test `Run_OverlayKeyHandler_CancelPendingG` (RED: `g h g` must NOT fire `MoveToFirst`); existing `Run_OverlayKeyHandler_*` tests stay GREEN.
- **Fails-if:** `g h g` triggers `gg` (MoveToFirst); `prompt-motion key=... caret=...` changes.

**BP-13 — `ResultMapper` byDisplay → `Ordinal` (D12)**
- **Files:** `Telescope/Overlay/Utils/ResultMapper.cs:34`.
- **Change:** the byDisplay map groups with `OrdinalIgnoreCase` → case-colliding duplicates ("Foo.cs"/"foo.cs") map to the wrong payload. Use `Ordinal`.
- **Verify-with:** new test `Run_ResultMapper_OrdinalCase` (Foo.cs/foo.cs map to distinct payloads; Enter opens the right file); existing `Run_ResultMapper_*` tests stay GREEN.
- **Fails-if:** a case-colliding duplicate opens the wrong file; `result-mapper unknown display: {display}` changes.

### Phase 3 — Finder cache sharing (D7)

**BP-14 — Inject ONE shared `FileContentCache` from `TelescopeController` (D7)**
- **Files:** `Telescope/Controller/TelescopeController.cs`, `Telescope/Finders/GrepFinder.cs:28`, `Telescope/Finders/CodeIssuesFinder.cs:40`, `Telescope/Finders/FzfFinder.cs:33`.
- **Change:** `ProjectFileCache` is ALREADY shared (injected in all three ctors); only `FileContentCache(500)` is per-instance (3×500-entry caches). Construct ONE `FileContentCache` in `TelescopeController` and inject it into all three finders (MEF/DI wiring — the finder ctors take the shared instance).
- **Verify-with:** new test `Run_FileContentCache_Shared` (all three finders share one instance — same reference); existing `Run_GrepFinder_*`/`Run_FzfFinder_*`/`Run_CodeIssuesFinder_*` tests stay GREEN.
- **Fails-if:** 3×500-entry independent caches; `grep hits=...`/`fzf hits=...` change.

### Phase 4 — WindowManager + navigation (A1, A6, A7, C7)

**BP-15 — Delete the eager controller registration loop, keep the lazy `_defaultControllers` cache (A1)**
- **Files:** `MyExtension/Package/MyExtensionPackage.cs:143-150` (delete the loop), `MyExtension/ToolWindows/WindowManager.cs:31` (keep `_defaultControllers`).
- **Change:** NO native VS per-tool-window-type keyboard-controller mechanism exists — delete the eager registration loop; the lazy `_defaultControllers` cache is the single mechanism (`ResolveController` depends on it — the m22 same-instance invariant).
- **Verify-with:** NeoVisual.Tests `Run_GetController_SameInstance` (RED if the lazy cache is deleted — `GetController` returns the same instance per type); `Run_ResolveController_*`/`Run_DefaultControllerFor_*` stay GREEN.
- **Fails-if:** `GetController` returns different instances per type; a per-type default controller silently diverges.

**BP-16 — `BuildActiveWindows` returns a COPY of `_cachedLinked` (A6)**
- **Files:** `MyExtension/Navigation/WindowNavigator.cs:26,107`.
- **Change:** `_cachedLinked` is a static mutable list keyed on object refs; `BuildActiveWindows` returns the SAME list instance to every navigator. Return a copy (preserves the cache, kills aliasing).
- **Verify-with:** NeoVisual.Tests `Run_BuildActiveWindows_ReturnsCopy` (mutating the returned list does not mutate the cache; two calls return distinct instances).
- **Fails-if:** a navigator mutates the shared cached list; `navigate direction=...` changes.

**BP-17 — `IsTextInputType` derives from a single classification source (A7)**
- **Files:** `MyExtension/ToolWindows/GeneralToolWindowController.cs:81`.
- **Change:** the hardcoded switch must be manually kept in sync with the `ToolWindowType` enum + `ToolWindowTypeResolver` GUID map — derive `IsTextInputType` from a single classification source (e.g. a `ToolWindowTypeResolver`-owned classification table).
- **Verify-with:** NeoVisual.Tests `Run_IsTextInputType_Classification` (every text-input type resolves consistently; a new text-input type is classified without a second switch).
- **Fails-if:** a new text-input type silently starts in normal mode (hjkl inject arrows); `toolwindow-enter-input`/`toolwindow-exit-input` change.

**BP-18 — Handle/log the `GetGuidProperty` HRESULT (C7)**
- **Files:** `MyExtension/ToolWindows/WindowManager.cs:341`.
- **Change:** the `GetGuidProperty` HRESULT is discarded → silent `_type = Unknown` on COM failure. Check the HRESULT and log the failure (e.g. `[NeoVisual] window type probe failed: {msg}`) instead of silently defaulting.
- **Verify-with:** NeoVisual.Tests `Run_GetGuidProperty_HResult` (a failed HRESULT is logged, not silently `Unknown`); existing `Run_ToolWindowType_*` tests stay GREEN.
- **Fails-if:** a COM failure silently sets `_type = Unknown` with no log.

### Phase 5 — Preview + Error List perf (A2, A3)

**BP-19 — Cache the preview text keyed on `ITextSnapshot.Version.VersionNumber` (A2)**
- **Files:** `MyExtension/Package/Utils/PreviewEditorHost.cs:100`.
- **Change:** `Show` materializes the whole preview buffer via `CurrentSnapshot.GetText()` on every call, even on mtime-cache hit. Extract a pure version→text cache helper; only re-read on rebuild.
- **Verify-with:** NeoVisual.Tests `Run_PreviewTextCache_VersionKeyed` (same snapshot version → cached text; new version → re-read); existing `Run_PreviewEditorHost_*` tests stay GREEN.
- **Fails-if:** a full-buffer `GetText()` per selection move; `preview file=...` changes.

**BP-20 — Short-TTL/bounded Error List scan with the cache decision in a pure helper (A3)**
- **Files:** `MyExtension/Package/Utils/ErrorListGatherer.cs:45-46`.
- **Change:** DTE `ErrorItems` has NO version counter — a count-keyed cache is weak (same count, different items after a build). Use a short-TTL cache or a bounded scan; the cache decision lives in a pure helper.
- **Verify-with:** NeoVisual.Tests `Run_ErrorListCacheDecision_TTL` (the pure helper decides cache-hit vs re-scan; a stale cache expires after the TTL); existing `Run_DiagnosticNavigator_*` tests stay GREEN.
- **Fails-if:** O(n) COM `ErrorItems.Item(i)` reads per `],e`/`[,e`/`],w`/`[,w` press; `diagnostic-nav direction=...` changes.

### Phase 6 — Tool-window controllers (A4, A5, A8, A9, A10, A11, C2, C3, C6)

**BP-21 — `TryMove` delegates to `ToolWindowControllerBase.TextMotion` (A4)**
- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs:193`.
- **Change:** `TryMove` re-implements the text-input routing block `ToolWindowControllerBase.TextMotion` already provides. Delegate, preserving the N21 side effect (`enteredInputMode → EnterInputMode()`) + the tree h/l guard.
- **Verify-with:** NeoVisual.Tests `Run_TryMove_DelegatesToTextMotion` (search-box motions route through the shared block; the N21 side effect + tree h/l guard preserved); existing `Run_SolutionExplorer_*` tests stay GREEN.
- **Fails-if:** search-box motions diverge from the shared block; the tree h/l guard breaks.

**BP-22 — Correct the `TextMotionHelper` WPF comment / read a bounded window (A5)**
- **Files:** `MyExtension/ToolWindows/Utils/TextMotionHelper.cs:107`.
- **Change:** the WPF path reads `focusedBox.Text` (a full-buffer copy) before slicing — the R18 "avoids the O(n) copy" claim is only half-realized. Correct the comment or read a bounded window.
- **Verify-with:** NeoVisual.Tests `Run_TextMotionHelper_BoundedRead` (the WPF path reads a bounded window, not the full buffer); existing `Run_TextMotionHelper_*` tests stay GREEN.
- **Fails-if:** a full-buffer `.Text` copy per motion; `text-motion key=... caret=...` changes.

**BP-23 — Extract the `HandleKey` tool-window routing block (A8)**
- **Files:** `MyExtension/Input/InputHandler.cs:267`.
- **Change:** `HandleKey` is a ~20-branch hot path with a 6-arg `ShouldRouteToolWindowKey` call — extract the tool-window routing block into a private method (behavior-preserving).
- **Verify-with:** NeoVisual.Tests `Run_HandleKey_RoutingBlockExtracted` (behavior unchanged — the extracted block routes identically); existing `Run_InputHandler_*` tests stay GREEN.
- **Fails-if:** tool-window routing behavior changes; `toolwindow-move key=...` changes.

**BP-24 — `_sessionMru` → LinkedList or cap (A9)**
- **Files:** `MyExtension/Package/Utils/RecentFilesGatherer.cs:158`.
- **Change:** `List.Remove` + `Insert(0, …)` per `DocumentOpened` is O(n) each, unbounded. Use a `LinkedList` (O(1) remove-first) or cap the list.
- **Verify-with:** NeoVisual.Tests `Run_RecentFilesMru_LinkedList` (the MRU is O(1) per open and bounded); existing `Run_RecentFilesGatherer_*` tests stay GREEN.
- **Fails-if:** O(n) per open; an unbounded `_sessionMru`.

**BP-25 — `FocusKeeper` per-controller or owner-check (C2)**
- **Files:** `MyExtension/ToolWindows/Utils/FocusKeeper.cs:17`.
- **Change:** `_current` is a static `DispatcherTimer` shared across controllers; `Run` stops any prior keeper regardless of owner. Make it per-controller or add an owner-check.
- **Verify-with:** NeoVisual.Tests `Run_FocusKeeper_OwnerCheck` (a second controller's `Run` does not stop the first's keeper); existing `Run_FocusKeeperSchedule_*` tests stay GREEN.
- **Fails-if:** one controller's `Run` stops another's keeper.

**BP-26 — Cache/bound the box-walk (C3)**
- **Files:** `MyExtension/Input/InputHandler.cs:338`.
- **Change:** `IsFocusedTextBoxInCurrentToolWindow()` (COM `GetProperty(VSFPROPID_DocView)` + visual-tree walk) runs on every shift+key routed to a tool window — cache the fact on focus-change events (the M1 pattern).
- **Verify-with:** NeoVisual.Tests `Run_BoxWalk_Cached` (the COM+visual-tree walk runs once per focus change, not per key); existing `Run_InputHandler_*` tests stay GREEN.
- **Fails-if:** a COM walk per shift+key; `toolwindow-move key=...` changes.

**BP-27 — Reset `_focusKeeper` to null after dispose (C6)**
- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs:31`.
- **Change:** `_focusKeeper` retains a disposed handle, never reset to null. Reset it to null after dispose.
- **Verify-with:** NeoVisual.Tests `Run_FocusKeeper_ResetAfterDispose` (after dispose, `_focusKeeper` is null; a re-run creates a fresh keeper); existing `Run_SolutionExplorer_*` tests stay GREEN.
- **Fails-if:** a disposed handle is reused.

**BP-28 — Dedupe the DocView walk-up loop (A10)**
- **Files:** `MyExtension/ToolWindows/WindowManager.cs:127`.
- **Change:** the "walk up to DocView content" loop is duplicated in `ComputeTextInputSurfaceFocused` + `IsFocusedTextBoxInCurrentToolWindow` — extract one shared helper.
- **Verify-with:** NeoVisual.Tests `Run_DocViewWalk_SingleSource` (both callers use the shared helper); existing `Run_ToolWindow_*` tests stay GREEN.
- **Fails-if:** the duplicated loop remains; `toolwindow-move key=...` changes.

**BP-29 — Single `CurrentController` resolution per key-down (A11)**
- **Files:** `MyExtension/Input/InputHandler.cs:453`.
- **Change:** `IsKeyOfInterest` resolves `_windowManager.CurrentController` twice per key-down — resolve once.
- **Verify-with:** NeoVisual.Tests `Run_IsKeyOfInterest_SingleResolve` (one `CurrentController` resolution per key-down); existing `Run_InputHandler_*` tests stay GREEN.
- **Fails-if:** double resolution per key-down; `[Hook]` lines change.

### Phase 7 — Vim mode contract (C1, C5)

**BP-30 — Extend `VimModeClassifier`'s name table + document `vim-mode=Unknown` + the numeric fallback (C1 — M-M7)**
- **Files:** `MyExtension/Vim/Utils/VimModeClassifier.cs:27`, `MyExtension/Vim/VimModeTracker.cs:138`, `AGENTS.md` (the `vim-mode=` contract line).
- **Change:** EXTEND the name table to the common extra VsVim modes — Visual, Command, VisualBlock, Select — replacing the numeric `vim-mode=<n>` for those; document `vim-mode=Unknown` (focus loss) as a legitimate token + the numeric fallback in AGENTS.md. The `vim-mode=Insert|Normal|Replace` contract is unchanged.
- **Verify-with:** NeoVisual.Tests `Run_VimModeClassifier_ExtraModes` (Visual/Command/VisualBlock/Select emit named tokens, not numerics); `Run_VimModeState_FocusLoss` (focus loss emits only the documented `Unknown`); the harness's `vim-mode=Insert`/`vim-mode=Normal` assertions (test-e2e.ps1:1494,1514) stay valid; e2e E2E-CR45-3 (`neovisual-editor-insert` + a named-mode assertion).
- **Fails-if:** `vim-mode=<n>` for Visual/Command; `vim-mode=Insert|Normal|Replace` changes; a strict/negative harness assertion over the mode line breaks.

**BP-31 — Document the `IsEditorFocused` fail-open risk (C5)**
- **Files:** `MyExtension/Vim/VimModeTracker.cs:39` (comment), `AGENTS.md` (the FocusGuard section).
- **Change:** do NOT broaden `IsEditorFocused` (it would track the Telescope preview view — deliberately non-Editable — and flip the flag while the overlay preview is focused). DOCUMENT the fail-open risk (a non-text editor with a stale `IsToolWindow` fails the FocusGuard OPEN).
- **Verify-with:** doc-content lint PASS; existing `Run_FocusGuard_*` tests stay GREEN.
- **Fails-if:** `IsEditorFocused` is broadened; the fail-open risk is undocumented.

### Phase 8 — InjectedKeyGuard (C4)

**BP-32 — Document the accepted risk (C4)**
- **Files:** `MyExtension/Hooks/Utils/InjectedKeyGuard.cs:83`.
- **Change:** `TryConsume` is keyed by VK+TTL only and cannot distinguish injected from physical — a dropped injected event could consume the next physical same-VK within 1s (theoretical; `keybd_event` queues synchronously). Document the accepted risk. No behavior change.
- **Verify-with:** the existing `Run_InjectedKeyGuard_*` tests stay GREEN + the documented-risk comment.
- **Fails-if:** a behavior change to `TryConsume`.

### Phase 9 — Harness (T1, T3, T4, T5, T6)

**BP-33 — `neovisual-window-nav` asserts the navigation OUTCOME (T1)**
- **Files:** `tools/harness/test-e2e.ps1:698-709`.
- **Change:** assert `navigate activated index=\d+` per chord (NOT "no no-op at all" — some directions may legitimately no-op depending on the scratch layout).
- **Verify-with:** e2e E2E-CR45-1 — `Assert-NewLogLine ... 'navigate activated index=\d+'` after each Ctrl+H/J/K/L chord.
- **Fails-if:** the scenario passes with every navigation a no-op; `navigate activated index=` never appears.

**BP-34 — `Assert-NoSeedLeak` throws in a full (non-reuse) run when the expected tree is absent (T3)**
- **Files:** `tools/harness/test-e2e.ps1:551-562` (NOT harness-common.ps1).
- **Change:** the "skips gracefully" path is a cannot-fail path in the write-leak guard — throw in a full (non-reuse) run when the expected-result tree or scratch dir is absent.
- **Verify-with:** e2e E2E-CR45-4 (the `seed-leak` scenario runs last and must not skip).
- **Fails-if:** `seed-leak` skips gracefully in a full run; a write leak goes undetected.

**BP-35 — `Wait-LogLine`'s `$searchedTo` becomes a persistent cursor preserving the fixed-baseline contract (T4)**
- **Files:** `tools/harness/harness-common.ps1:168-190`.
- **Change:** the per-call local cursor makes wait helpers O(calls × window). Make `$searchedTo` a persistent cursor that searches after the baseline WITHOUT advancing on match (AGENTS.md forbids an advancing cursor).
- **Verify-with:** e2e E2E-CR45-4 (the fixed-baseline contract holds — an assert may re-confirm a line another helper already saw).
- **Fails-if:** the cursor advances on match (breaks the fixed-baseline contract); a stale line satisfies a later assertion.

**BP-36 — `telescope-navigate` uses the LogCache tail-read (T5)**
- **Files:** `tools/harness/test-e2e.ps1:624`.
- **Change:** the scenario reads the whole log with `Get-Content`, bypassing the LogCache tail-read machinery — switch to the tail-read.
- **Verify-with:** e2e E2E-CR45-4 (`telescope-navigate` passes via the LogCache tail-read).
- **Fails-if:** a whole-log `Get-Content` read remains; `telescope-navigate` RED.

**BP-37 — The runner enforces the suite-order invariants (T6)**
- **Files:** `tools/harness/test-e2e.ps1:53-66`.
- **Change:** the suite order-dependency invariants (seed-leak last, editor-insert before it) are not enforced by the runner — enforce them.
- **Verify-with:** e2e E2E-CR45-4 (the runner enforces the order; a mis-ordered run fails fast).
- **Fails-if:** `seed-leak` runs before `neovisual-editor-insert`; the order invariants are unenforced.

### Phase 10 — Pane contract tests (T2)

**BP-38 — `IPane`-contract tests with a FAKE pane (T2)**
- **Files:** `tests/Telescope.Tests/Program.cs`.
- **Change:** the three concrete panes (`PromptPane`/`ListPane`/`PreviewPane`) are thin WPF shells — test the `IPane` contract + focus model with a fake pane (Activate/Deactivate, content wiring, registry order).
- **Verify-with:** new tests `Run_IPane_Contract_Activate`, `Run_IPane_Contract_Deactivate`, `Run_IPane_Contract_RegistryOrder` (fake pane); existing `Run_FocusTarget_*` tests stay GREEN.
- **Fails-if:** a pane `Activate`/`Deactivate` regression is invisible to the suite.

### Phase 11 — Docs (DOC1-DOC6)

**BP-39 — AGENTS.md "43 executed GREEN / telescope-recent pending" → 44/44 GREEN (DOC1)**
- **Files:** `AGENTS.md:145-146`, `.opencode/skills/vs-extension-dev/SKILL.md`, `docs/spec.md`.
- **Change:** refresh the stale scenario-count claims to 44/44 GREEN.
- **Verify-with:** doc-content lint PASS (`pwsh tools/lint/check-doc-content.ps1`).
- **Fails-if:** doc-content lint flags the stale count.

**BP-40 — spec.md's 43-vs-44 scenario list (DOC2)**
- **Files:** `docs/spec.md:390`.
- **Change:** the list has 43 scenarios but claims 44 — add `neovisual-git-bindings`.
- **Verify-with:** doc-content lint PASS.
- **Fails-if:** doc-content lint flags the missing scenario.

**BP-41 — progress.md's pending-queue run-order block (DOC3)**
- **Files:** `docs/progress.md:381-388`.
- **Change:** the pending-queue block still shows gap 11/feature 7/gap 4 pending though all GREEN — refresh.
- **Verify-with:** doc-content lint PASS.
- **Fails-if:** doc-content lint flags the stale block.

**BP-42 — Prior-review "still open" annotations N54/N55/W12 → resolved (DOC4)**
- **Files:** `docs/reviews/code-review.md:70-71`, `docs/reviews/architecture-review.md:46`.
- **Change:** mark the resolved findings as resolved.
- **Verify-with:** doc-content lint PASS.
- **Fails-if:** doc-content lint flags the stale annotations.

**BP-43 — code-review.md's stale test counts 153/163 → 268/191 (DOC5)**
- **Files:** `docs/reviews/code-review.md:191`.
- **Change:** refresh the test counts.
- **Verify-with:** doc-content lint PASS.
- **Fails-if:** doc-content lint flags the stale counts.

**BP-44 — spec.md §7's missing git-bindings bullet (DOC6)**
- **Files:** `docs/spec.md:470-555`.
- **Change:** add the git leader bindings to the done-feature list.
- **Verify-with:** doc-content lint PASS.
- **Fails-if:** doc-content lint flags the missing bullet.

### Finding coverage (45/45)

| finding | sev | BP step(s) | finding | sev | BP step(s) |
|---|---|---|---|---|---|
| D1 | major | BP-1 | A6 | minor | BP-16 |
| D2 | major | BP-1 | A7 | minor | BP-17 |
| D3 | major | BP-5 | A8 | minor | BP-23 |
| D4 | major | BP-9 | A9 | minor | BP-24 |
| D5 | major | BP-10 | A10 | nit | BP-28 |
| D6 | minor | BP-2 | A11 | nit | BP-29 |
| D7 | minor | BP-14 | C1 | minor | BP-30 |
| D8 | minor | BP-7 | C2 | minor | BP-25 |
| D9 | minor | BP-11 | C3 | minor | BP-26 |
| D10 | minor | BP-12 | C4 | minor | BP-32 |
| D11 | minor | BP-6 | C5 | minor | BP-31 |
| D12 | minor | BP-13 | C6 | nit | BP-27 |
| D13 | nit | BP-4 | C7 | nit | BP-18 |
| D14 | nit | BP-3 | T1 | major | BP-33 |
| D15 | nit | BP-8 | T2 | minor | BP-38 |
| A1 | major | BP-15 | T3 | minor | BP-34 |
| A2 | major | BP-19 | T4 | nit | BP-35 |
| A3 | major | BP-20 | T5 | nit | BP-36 |
| A4 | minor | BP-21 | T6 | nit | BP-37 |
| A5 | minor | BP-22 | DOC1 | minor | BP-39 |
| | | | DOC2 | minor | BP-40 |
| | | | DOC3 | minor | BP-41 |
| | | | DOC4 | minor | BP-42 |
| | | | DOC5 | nit | BP-43 |
| | | | DOC6 | nit | BP-44 |

## Verification Trace

> Maps each failing test/gate to its implicated BP-n step and the expected diagnostic that
> proves it. **Known-RED allowlist: NONE** — the baseline is all-GREEN (44/44 e2e, Telescope
> 268, NeoVisual 191); the `telescope-goto` flaky ledger is CLOSED. Do NOT report any of the
> below as a pre-existing regression. **The e2e rows (E2E-CR45-1..4) are QUEUED gates** — the
> unit rows are the primary Verify-with at this plan's VERIFY; the e2e rows are created/
> executed on a capable machine after this plan is GREEN.

| failing test / scenario | implicated steps | expected diagnostic |
|---|---|---|
| `Run_FocusTarget_*` (23 tests, Program.cs:4000-4135+) | BP-1, BP-2, BP-3 | `[Telescope] focus target=Input|List|Preview` + `[Telescope] focus no-op: no pane {direction} from {pane}` UNCHANGED |
| `Run_PaneNavEngine_*` (7 tests) | BP-1 | migrated to `Run_FocusTarget_*` equivalents (the pinned adjacency tie-break survives) |
| `Run_FzfFilter_*` (kill-on-cancel / probe-once / timeout-CTS) | BP-5, BP-6, BP-8 | `[Telescope] fzf hits=...` / `[Telescope] fzf filter failed: {msg}` UNCHANGED |
| `Run_QuoteArg_*` (5 tests) | BP-7 | (none — pure quoter) |
| `Run_PreviewCaretMap_Clamp` | BP-9 | `[Telescope] preview caret=... line=...` UNCHANGED |
| `Run_GrepFinder_ScanFileBackground` | BP-10 | `[Telescope] grep hits=...` UNCHANGED |
| `Run_ResultsFormatter_RenderedTextLength` | BP-11 | `[Telescope] results count=... boxText=<re-pinned>` (M-M7) |
| `Run_OverlayKeyHandler_CancelPendingG` | BP-12 | `[Telescope] prompt-motion key=... caret=...` UNCHANGED |
| `Run_ResultMapper_OrdinalCase` | BP-13 | `[Telescope] result-mapper unknown display: {display}` UNCHANGED |
| `Run_FileContentCache_Shared` | BP-14 | `[Telescope] grep hits=...` / `[Telescope] fzf hits=...` UNCHANGED |
| `Run_GetController_SameInstance` | BP-15 | `[NeoVisual] toolwindow-move key=...` UNCHANGED |
| `Run_BuildActiveWindows_ReturnsCopy` | BP-16 | `[NeoVisual] navigate direction=...` UNCHANGED |
| `Run_IsTextInputType_Classification` | BP-17 | `[NeoVisual] toolwindow-enter-input` / `toolwindow-exit-input` UNCHANGED |
| `Run_GetGuidProperty_HResult` | BP-18 | `[NeoVisual] window type probe failed: {msg}` (NEW) — `[NeoVisual] window rect unavailable; using empty rect` UNCHANGED |
| `Run_PreviewTextCache_VersionKeyed` | BP-19 | `[Telescope] preview file=...` UNCHANGED |
| `Run_ErrorListCacheDecision_TTL` | BP-20 | `[NeoVisual] diagnostic-nav direction=...` UNCHANGED |
| `Run_TryMove_DelegatesToTextMotion` | BP-21 | `[NeoVisual] solution-explorer ...` / `[NeoVisual] text-motion key=...` UNCHANGED |
| `Run_TextMotionHelper_BoundedRead` | BP-22 | `[NeoVisual] text-motion key=... caret=...` UNCHANGED |
| `Run_HandleKey_RoutingBlockExtracted` | BP-23 | `[NeoVisual] toolwindow-move key=...` UNCHANGED |
| `Run_RecentFilesMru_LinkedList` | BP-24 | `[Telescope] recent files gathered count=...` UNCHANGED |
| `Run_FocusKeeper_OwnerCheck` | BP-25 | `[NeoVisual] solution-explorer ...` UNCHANGED |
| `Run_BoxWalk_Cached` | BP-26 | `[NeoVisual] toolwindow-move key=...` UNCHANGED |
| `Run_FocusKeeper_ResetAfterDispose` | BP-27 | `[NeoVisual] solution-explorer ...` UNCHANGED |
| `Run_DocViewWalk_SingleSource` | BP-28 | `[NeoVisual] toolwindow-move key=...` UNCHANGED |
| `Run_IsKeyOfInterest_SingleResolve` | BP-29 | `[Hook]` lines UNCHANGED |
| `Run_VimModeClassifier_ExtraModes` / `Run_VimModeState_FocusLoss` | BP-30 | `[NeoVisual] vim-mode=Visual|Command|VisualBlock|Select` (M-M7) + `vim-mode=Unknown` documented |
| `Run_FocusGuard_*` | BP-31 | `[NeoVisual] vim-mode=...` UNCHANGED |
| `Run_InjectedKeyGuard_*` | BP-32 | UNCHANGED |
| e2e E2E-CR45-1 `neovisual-window-nav` | BP-33 | `[NeoVisual] navigate activated index=\d+` per chord |
| e2e E2E-CR45-2 `telescope-focus-panes` | BP-1, BP-2, BP-3 | `[Telescope] focus target=` / `focus no-op:` UNCHANGED |
| e2e E2E-CR45-3 `neovisual-editor-insert` | BP-30 | `[NeoVisual] vim-mode=Insert` + a named-mode token |
| e2e E2E-CR45-4 full 44-scenario suite | BP-5..BP-44 | all diagnostics UNCHANGED except the M-M7 literals (C1/D9) |
| doc-ref lint (`check-doc-refs.ps1`) | BP-1, BP-4, BP-30, BP-31, BP-39..BP-44 | 0 unresolved backticked refs |
| doc-content lint (`check-doc-content.ps1`) | BP-39..BP-44 | 12/12 PASS |

## Execution Log

> **Attempt 1 (2026-10-05, unit-only lane, e2e DEFERRED).** RED → BUILD → (all GREEN at the
> unit level; the e2e gates E2E-CR45-1..4 stay QUEUED in `docs/e2e-queue.md`).

**RED (e2e-test-builder, 3 dispatches — the first two hit the step limit):** wrote 29 new
unit tests across `tests/Telescope.Tests` (20) + `tests/NeoVisual.Tests` (9). RED proven:
Telescope compile-RED (CS0117/CS1061/CS1503/CS1729 — `ChordDirection`, `PendingTimeoutCount`,
`ScanFile`, `CancelPendingG`, shared-cache finder ctors) + behavior-RED (`ResultMapper`
Ordinal, `FzfFilter` kill-before-write, `VimModeClassifier` extra modes, the pane-host
collapse reflection tests, `RenderedTextLength` re-pin); NeoVisual compile-RED
(`IsTextInputType`, `WindowTypeProbe`, `PreviewTextCache`, `ErrorListCacheDecision`,
`RecentFilesMru`, `ResetFocusKeeper`) + behavior-RED (`VimModeClassifier_ExtraModes`).
8 tests not writable (no hermetic seam — COM/WPF/AsyncPackage-coupled, verified). 1 deviation:
`Run_ResultsFormatter_RenderedTextLength` written as behavior-RED (the 4 legacy
`Run_ResultsFormatter_RenderedTextLength_*` tests updated by the build as part of BP-11).

**BUILD (build-agent, 6 dispatches — step-limit driven):**
- BP-1/2/3/11/12/13 + BP-4 (deleted `PaneNavigationEngine.cs` + `TryDispatch.cs`): the pane-host
  collapse into `FocusTargetModel` (single resolver + `ChordDirection`), `PaneHost` single-owner,
  `CancelPendingG`, `ResultMapper` Ordinal, `RenderedTextLength` re-pin + the 7
  `Run_PaneNavEngine_*` → `Run_FocusTarget_*` migration + the 4 legacy RenderedTextLength tests.
- BP-5..BP-14 (Telescope-side): `FzfFilter` kill-before-write + `_probed`/`_value` split +
  `PendingTimeoutCount` + timeout CTS + `QuoteArg` doc; `ApplyPreviewCaret` clamp;
  `GrepFinder.ScanFile` internal static; `TryPromptMotion` → `CancelPendingG`; shared
  `FileContentCache` finder ctors + `TelescopeController` wiring. **Telescope.Tests 288/288.**
  (One test-authoring fix: the BP-5 block.cmd used `ping` which spawned a grandchild holding the
  pipe read end — changed to an infinite cmd.exe loop so killing the process unblocks the write.)
- BP-15..BP-20 + BP-28 (MyExtension core): eager controller loop deleted; `BuildActiveWindows`
  returns a copy; `ToolWindowTypeResolver.IsTextInputType`; `WindowTypeProbe.ShouldLogFailure` +
  the `window type probe failed` log; `PreviewTextCache`; `ErrorListCacheDecision`; the shared
  DocView walk-up helper.
- BP-21..BP-27 + BP-29 + BP-30 (controllers + vim mode): `TryMove` delegates to
  `ToolWindowControllerBase.TextMotion`; `TextMotionHelper` comment; `HandleKey` routing block
  extracted; `RecentFilesMru`; `FocusKeeper` per-controller; box-walk cached; `ResetFocusKeeper`;
  single `CurrentController` resolve; `VimModeClassifier` extra modes (Command/Visual/
  VisualBlock/Select). **NeoVisual.Tests 200/200.**
- BP-31..BP-37 (harness + code comments): the `IsEditorFocused` fail-open + `InjectedKeyGuard`
  risk docs; the harness T1/T3/T4/T5/T6 changes (e2e-queued — made, not run; `-List` parses).
- BP-38 (IPane contract tests) — written by RED, GREEN. BP-39..BP-44 + BP-1 doc propagation —
  done by the hub (doc-ref lint 0 unresolved; doc-content lint 12/12; the DOC-67-2 seam list
  updated for the deleted `TryDispatch`).

**VERIFY (verification-agent):** pending.

**Cost:** `delegations: 11 | VS boots: 0 | iterations: 0` (unit-only lane; the high delegation
count is step-limit-driven — each build-agent dispatch executed a bounded chunk of the 44-BP
plan).
