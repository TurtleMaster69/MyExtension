# HANDOFF — code-review-hub session 2026-09-29-065433

> **READ THIS FIRST.** This session's context was clogged after 6 review rounds, so the
> audit is being resumed in a FRESH session. This file is the complete, self-contained
> resume brief. Do NOT re-derive anything below — trust it, verify only what you act on.
> Session dir: `.opencode/workspaces/code-review-hub/sessions/code-review-hub-20260929-065433/`
> (state.md / tasks.md / log.md / HANDOFF.md). New session = new dir under `sessions/`.

---

## 1. Objective (unchanged)

Review the implemented fixes in the MyExtension VSIX — the **Architecture consolidation**
(commit `b945ab8`, ~30 seams collapsed) plus **F22/F12/F15** (`eff04e4`/`3b43430`/`33b7ef4`)
plus **KeyInjection.SimulateOnly** — then run a **10-round review**:

- **Rounds 1–5 (correctness):** logic / concurrency / interop / security-input / edge-robustness
- **Rounds 6–10 (simplification):** duplication / merge-systems / ease-of-use / naming-conventions / cross-cutting

Each round splits the repo into **10 sections**, one `arch-auditor` (simplification rounds)
or `code-review-worker` (correctness rounds) per section, dispatched in parallel.
Deliverable: consolidated `docs/code-review.md` + backlog findings filed to `docs/progress.md`
(append-only, only after user approval via the `question` tool).

**User decisions (already made, do not re-ask):** 10 rounds total. Section split = the hub's
10-section split below; "one section per agent, do not combine sections unless it logically
makes sense."

## 2. Baseline (verified 2026-09-29)

- `dotnet build`: **0 errors / 111 warnings** (mostly VSTHRD010 analyzer noise).
- `dotnet run --project tests/Telescope.Tests`: **84/84 passing**.
- `dotnet run --project tests/NeoVisual.Tests`: **81/81 passing**.
- Test counts drift upward — never judge against a remembered count; use `--list`.

## 3. What is COMPLETE

- Recon digest (trailmark-recon, one task) — see §6.
- **Rounds 1–5 (correctness): 352 raw findings** — R1=55 (1 crit, 3 major), R2=65 (7 major),
  R3=57 (5 major), R4=76 (2 crit, 13 major), R5=99 (1 crit, 20 major).
- **Round 6 (duplication): ~30 findings** (mostly major/minor). Key ones:
  - KeyToString switch duplicated: `InputHandler.cs:469` vs `LeaderSequenceMatcher.cs:97`.
  - DefaultControllerKeys vs hardcoded H/J/K/L: `InputHandler.cs:318`.
  - FocusKeeper DispatcherTimer idiom 2×: `SolutionExplorerController.cs:114` and `:241`.
  - J/K arrow-inject+log duplicated vs `GeneralToolWindowController`.
  - `FindFirstSourceFileInItems` = 3rd DTE walker: `MyExtensionPackage.cs:285`.
  - `OpenReference`/`OpenImplementation` byte-identical (Telescope finders).
  - `FileFinder.CollectProjectFiles` near-verbatim copy of `ProjectFiles`.
  - `GrepFinder` re-walks instead of using `ProjectFileCache`.
  - `TryPromptMotion`/`HandlePreviewKey`/`ApplyPromptCaretStyle` duplicate `TextMotionHelper`.
  - `LogFileWriter` Write/WriteDebug byte-identical; Write/WriteLine; LogPath setters + facade pass-throughs.
  - Tests: duplicate MapMotion tests; temp-dir setup copy-pasted ~15×; LogFileWriter path save/restore ~8×.
  - Harness: Wait-NewLogLine* 3 near-identical bodies; Open-Telescope* 5 copies; SE-toggle loop 9×; seeded canonical content 2×.

## 4. What is PENDING (the fresh session's job)

1. **Round 7 (merge-systems)** — 10 parallel `arch-auditor` agents. Known cross-section
   consolidation candidates to feed the briefs: 3 DTE walkers (ProjectFiles / FileFinder /
   FindFirstSourceFileInItems), 3+ key→motion dispatch tables, 3 caret renderers
   (BlockCaretAdornment / ApplyPromptCaretStyle / TextMotionHelper), 2 focus keepers,
   log writers (NeoVisualLog vs LogFileWriter vs NeoVisualTraceListener), harness wait loops.
2. **Round 8 (ease-of-use / testability)** — 10 agents.
3. **Round 9 (naming/conventions)** — 10 agents. NOTE: `CardinalMovment` folder typo is
   INTENTIONAL — do not flag it.
4. **Round 10 (cross-cutting)** — 10 agents: whole-repo perf hazards, log-format drift across
   the two projects, net472/BCL consistency, namespace/folder hygiene, hook-path cost.
5. **Consolidate** all rounds (~380+ raw findings), dedupe, spot-verify high-impact claims
   against current code, prioritize Critical/Major/Minor/Nit.
6. **Write `docs/code-review.md`** (overwrite; header/date/scope/method, summary, findings
   table, detailed findings with file:line + why-it-bites + suggested fix, recommendations,
   filed-into-progress section).
7. **Ask via `question`** which findings to append to `docs/progress.md` (append-only;
   `neovim_hub` owns that file).

## 5. Confirmed CRITICAL findings (hub-verified, do NOT re-verify from scratch)

1. **`MyExtensionPackage.cs:92-101`** — per-type controller loop registers a
   `GeneralToolWindowController` for EVERY `ToolWindowType` including `SolutionExplorer`,
   overwriting the `SolutionExplorerController` registered at `:87` (dict assignment
   `_controllers[controller.Type] = controller`, `WindowManager.cs:78`). Solution Explorer
   loses o/Enter/r/m/a/g/i; all `neovisual-explorer-*` e2e would regress. Confirmed by 4
   independent workers + hub.
2. **`tools/test-e2e.ps1:1909`** — F38 INCOMPLETE: `$ok = ($result -ne $false)` sets `$ok=false`
   but `$failures` is appended ONLY in the `catch`, so a `$false`-returning scenario prints
   per-scenario FAILED but the aggregate gate `if ($failures.Count -gt 0)` at `:1915` is
   bypassed → "RESULT: PASS" + exit 0.
3. **`Telescope/TelescopeOverlay.cs:303`** — pending `ShowDialog()` after `CloseOverlay()`
   (fire-and-forget fzf task can still show a modal after the overlay closed).

## 6. Recon digest (trailmark-recon, 2026-09-29)

- nodes=1298, functions=514, classes=71, proxies=552 (42.5%), edges=2234, entrypoints=0.
- Complexity hotspots: SyntaxHighlighter.Segment(23), GeneralToolWindowController.IsTextInputType(19),
  InputHandler.HandleKey(17), TextMotionHelper.ApplyMotionToBox(14), OverlayKeyHandler.HandleNormal(14).
- Blast radius: InitializeAsync(85), TelescopeOverlay.OnPreviewKeyDown(51), VimModeTracker.OnViewGotFocus(35),
  TryMoveFocusedSurface(32), GatherImplementations(30).

## 7. Conventions & traps (give to every worker)

- **net472** — no modern BCL (`IReadOnlySet<T>` etc.); LangVersion 14, Nullable enabled.
- **UI-thread affinity mandatory** — every IVs*/DTE/EnvDTE call on main thread;
  `ThreadHelper.ThrowIfNotOnUIThread()`; hook marshals via JTF; never `.Result`/`.GetAwaiter().GetResult()`.
- **Diagnostics-as-contract** — `[Telescope]`/`[NeoVisual]`/`[Hook]` log lines are asserted by
  the e2e harness; a format change is a contract break.
- **`CardinalMovment` folder typo is intentional** — never flag it.
- **Trailmark API (CRITICAL):** `trailmark.parse.parse_directory` + `trailmark.query.QueryEngine`
  is BROKEN (AttributeError `find_node_id`). The working entry point is
  `QueryEngine.from_directory("C:\Users\hribara\MyExtension", language="c_sharp")`.
  This was logged in `.opencode/AGENT-FAILURES.md` (2026-09-29 entry). Tell every worker this.
- **Proxy trap:** bare `callers_of` returning 0 on a public/static member is SUSPECT, not dead
  code — query `proxy.unresolved:<Type>.<Member>` ids or cross-check `callees_of` from the
  caller side. Never report "dead code" from a bare 0.
- **No entrypoints** (VSIX) — do not load trailmark-finding-triage / trailmark-review-gate /
  graph-evolution; use callers_of/callees_of, paths_between, reachable_from, complexity_hotspots, blast radius.
- **S4 slice (VimModeTracker+WM+Package) has a pattern of empty worker returns** — if a
  delegation returns empty, re-dispatch ONCE with the tight `QueryEngine.from_directory` brief.

## 8. The 10-section split (regenerate file lists by enumerating at dispatch time)

- **S1** `MyExtension/` core: GlobalKeyboardHook.cs, InputHandler.cs, InjectedKeyGuard.cs,
  KeybindingConfig.cs, VimModeTracker.cs, PopupNavigation.cs, WindowManager.cs,
  BlockCaretAdornment.cs, KeyInjection.cs, MyExtensionPackage.cs, TelescopeCommand.cs,
  ToolWindowTypeResolver.cs
- **S2** `CardinalMovment/`: WindowMatrix.cs, WindowControlAdapter.cs, IVsFrameView.cs,
  IVsUIWindowFrameExtractor.cs, UtilityMethods.cs, CardinalNavigationConstants.cs,
  RectCoordinate.cs, LinqExtensionMethods.cs
- **S3** `MyExtension/ToolWindows/`: IToolWindowController.cs, GeneralToolWindowController.cs,
  TextInputToolWindowController.cs, SolutionExplorerController.cs, HierarchyResolver.cs,
  TextMotionHelper.cs
- **S4** VimModeTracker + WindowManager + MyExtensionPackage (cross-file seams)
- **S5** `Telescope/` overlay: TelescopeController.cs, TelescopeOverlay.cs, TelescopeFinder.cs,
  IQueryFinder.cs, OverlayKeyHandler.cs
- **S6** `Telescope/` finders: FileFinder.cs, CodeIssuesFinder.cs, CodeIssue.cs, GrepFinder.cs,
  GrepHit.cs, ReferencesFinder.cs, ReferenceHit.cs, ImplementationFinder.cs,
  ImplementationHit.cs, ProjectFiles.cs
- **S7** `Telescope/` preview-motion-fzf: TextMotionNavigator.cs, SyntaxHighlighter.cs,
  ResultsFormatter.cs, FzfFilter.cs, PreviewRenderer.cs
- **S8** `Telescope/` logging: DiagnosticLog.cs, NeoVisualLog.cs, LogFileWriter.cs,
  NeoVisualTraceListener.cs
- **S9** `tests/`: tests/Telescope.Tests/Program.cs, tests/NeoVisual.Tests/Program.cs
- **S10** `tools/`: tools/test-e2e.ps1, tools/iterate-telescope.ps1, tools/dte-command.ps1,
  tools/check-doc-refs.ps1, tools/harness-common.ps1

## 9. Worker brief template (five-field delegation contract)

Every brief carries: `TASK` (one-sentence deliverable) · `OBJECTIVE` (checkable outcome) ·
`SCOPE` (the ONLY paths it may touch) · `VERIFY` (cite the Trailmark query + result for every
structural claim) · `REPORT BACK` (≤1500-token digest with file:line). Plus: tool-call budget
(≤15, absolute 20), diminishing-returns clause, sentinel token (`DONE | <slice> | <count>
findings`), and the §7 conventions + §6 recon digest verbatim. Fresh workers only — never
resume via `task_id`. Worker-crash fallback: re-dispatch ONCE fresh; second failure escalates
via `question`.

## 10. Key MAJOR findings to carry forward (from R1–R5; verify against current code before re-reporting)

- `Telescope/TextMotionNavigator.cs:120` — Up() `LastIndexOf` with negative startIndex.
- `Telescope/PreviewRenderer.cs:141` — CaretToPointer null on blank line.
- `Telescope/SyntaxHighlighter.cs:198` — ReadQuoted `i+=2` overshoot.
- `MyExtension/CardinalMovment/WindowMatrix.cs:98` — AutoHides() outside try/catch; `:106` one-bad-frame aborts navigation (F7).
- `Telescope/FzfFilter.cs:113` — sync pipe write on UI thread (M12).
- `Telescope/GrepFinder.cs:78-96` — full sync scan per keystroke (M19).
- `tools/harness-common.ps1:92` — Send-Text uppercase typed lowercase (F41 partial).
- `tools/iterate-telescope.ps1:112` — blanket devenv kill (F42 partial).
- `Telescope/CodeIssuesFinder.cs:186` — Classify() mislabels.
- `MyExtension/ToolWindows/SolutionExplorerController.cs:295` — BuildForest nested-folder expansion gap.
- `MyExtension/LeaderSequenceMatcher.cs:63` — action() no try/catch in hook path.
- `MyExtension/ToolWindows/FocusGuard.cs:26` — isTextInputSurface reverse-leak.
- `MyExtension/VimModeTracker.cs:189,209,483-488` — stale typing flag, latch, `(int)value` cast.
- `Telescope/NeoVisualLog.cs:109,132-148` — logging races; `NeoVisualLog.Close` has 0 callers.
- `tools/dte-command.ps1:92` — no-timeout COM call.
- `tests/Telescope.Tests/Program.cs:149,286,774` — order-dependency, F43 false green, keybindings test reads user config.

## 11. FALSE POSITIVES already dropped (do NOT re-report)

- **GapTo formula swap** (`RectCoordinate.cs:47-50`) — call is `c.GapTo(active, ...)` so
  `this`=candidate, `other`=active; formulas match pre-consolidation semantics exactly.
  Worker confused receiver/argument roles.

## 12. Next steps (ordered, for the fresh session)

1. Read this HANDOFF.md + `docs/progress.md` + `.opencode/agent/code-review-hub.md` + session
   `state.md` (re-pin invariants).
2. Dispatch **Round 7 (merge-systems)** — 10 `arch-auditor` agents, §8 split, §9 brief, §7 conventions.
3. Dispatch Rounds 8, 9, 10 (one at a time, recording each in the new session's log.md).
4. Consolidate all rounds, dedupe, spot-verify, prioritize.
5. Write `docs/code-review.md`.
6. `question` → file approved findings into `docs/progress.md` (append-only).
