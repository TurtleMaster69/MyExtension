# Progress — MyExtension build queue

This file tracks the current build-loop state: known bugs, in-progress work, and
the pending feature queue. It is the working document the hub (`neovim_hub`)
reads at the start of every loop iteration.

> **Resume checkpoint:** the previous session checkpoint (`.opencode/PROGRESS.md`)
> has been superseded by this file.

> **Status:** ACTIVE · **Updated:** 2026-10-04 · **Last item:** the columns UX bugfix (no
h-scroll, all columns visible, min/max widths, logical shortening, column-relative window
width, the selection contrast) — GREEN 2026-10-04 (bugfix + UX lane, e2e ENABLED; full
41-scenario suite + both unit suites GREEN, zero flakes, 0 iterations)

## SESSION HANDOFF (2026-10-04) — read this first

> **Where we left off:** the build loop RESUMED and **Gap 3 (diagnostics navigation,
> `]`/`[` prefix) is GREEN 2026-10-04** — executed per the plan in
> `docs/implementation_plan.md` (6 phases + a verify-time Phase 7, 33 BP steps,
> gate-APPROVED). **E2E was RE-AUTHORIZED by the user mid-session** ("u dont need to defer
> them anymore") — the full 39-scenario suite ran GREEN (1 flaky recorded) and the queued
> gates E2E-GAP1-1..5 + E2E-GAP3-1..2 are discharged. The next item is **Gap 11 (git
> bindings)**. Nothing is in flight; the working tree is clean after the GREEN commit.

### User instructions that override the default loop (2026-10-04)

1. **E2E TESTS ARE DEFERRED — DO NOT RUN THEM.** The user said: *"we are again queuing e2e
   tests for later. never run the e2e tests until I tell you."* So for every item from now
   on: **do NOT boot the VS Experimental Instance, do NOT run
   `tools/harness/test-e2e.ps1`** (not even `-Tests <subset>`). This is the **unit-only
   lane** (the same lane used by the 51/98/67-findings plans). RED is proven at the **unit
   level only**; the e2e gates are **queued in `docs/e2e-queue.md`** with status QUEUED and
   run later on a VS-capable machine when the user says so. The harness-health no-VS
   self-checks (`-List`, `-SelfCheck`, `check-doc-refs.ps1`) are still allowed (they do not
   boot VS) — but the full/subset e2e runs are not.
   - **Consequence for the loop:** the feature lane's "e2e RED boots VS" step is replaced
     by "unit RED only + queue the e2e gate". A feature item still needs a RED unit test
     (or, where no hermetic seam exists, the `bugfix (no-seam)`-style stated reason + a
     queued e2e gate). VERIFY runs the affected unit project(s) only; the final gate runs
     **both unit projects** (no e2e).
   - **SUPERSEDED (2026-10-04, later):** the user re-authorized e2e (*"u dont need to
     defer them anymore"* / *"u also have permissions to run e2e now"*). From that point:
     e2e runs normally (the default loop's e2e gates apply); the queued gates were
     discharged at the Gap 3 VERIFY (full 39-scenario suite GREEN).
2. **NEW PLANNING TACTIC (user instruction 2026-10-04).** Instead of one planner writing
   the whole detailed plan, use a **two-stage, fan-out** approach:
   - **Stage 1 — general plan/architecture:** one `implementation-planner` task produces a
     **general plan / architecture** (high-level, section-structured — NOT BP-n steps).
   - **Stage 2 — split + parallel detail planning:** the **hub** splits that general plan
     into its sections and dispatches **multiple parallel** `implementation-planner` tasks,
     one per section, each expanding its section into detail.
   - **Stage 3 — aggregate:** the **hub** aggregates the section outputs into the final
     `docs/implementation_plan.md` (Build Plan + Verification Trace).
   - Rationale: the single-planner approach crashed twice on Gap 1; fan-out is more robust
     and parallelizes the detail work.
3. **Autonomous run scope (user instruction 2026-10-04):** work through **features 6-9 +
   the LazyVim gaps** in the **"smallest first"** order (below), then **which-key last**.
   The user will interrupt when they return.

### Run order (smallest first — user-chosen 2026-10-03)

`feature 6 (DONE) → gap 1 → gap 3 → gap 11 → feature 7 → gap 4 → gap 5 → gap 6 →
gap 10 → gap 9 → feature 8 (+gap 8) → feature 9 → which-key last`

### Decisions made this session (2026-10-04)

- **Gap 8 (quickfix):** native Error List data shown in a **custom Telescope-style view**;
  quick-fix actions folded into the **code-actions picker** (feature 8) — `Leader+C+A` shows
  all code actions for the selection/at-caret, **quick-fixes at the top**, **warning-fix
  first**.
- **Gap 9 (search/replace):** custom overlay with **two input fields** (search, replace);
  seed search from the selection, else the word at the caret; navigate hits; replace current;
  replace all.
- **Gap 10 (hover/signature):** custom **focusable** overlay over native QuickInfo/
  ParameterInfo, navigable with vim motions.
- **Gap 11 (git):** extend/reuse native — leader bindings for native git diff/blame/log/
  history (verify exact command names live).
- **Which-key (last):** build **BOTH** a **bottom-right non-focus-stealing popup overlay**
  (LazyVim-style — visually appears, does not steal focus) **AND an inline popup variant**,
  so both can be tested. Research is at `docs/plans/whichkey-research.md`.
- **Feature 9 (r/a/m overlay):** **attempt it** — research VS's reference-fixing
  rename/move API first (the open feasibility question).
- **Gaps 8-11 triage:** done (see the `[2026-10-03]` Decisions entry).

### Completed this session

- **Gap 3 — diagnostics navigation (`]`/`[` prefix)** — GREEN 2026-10-04 (e2e ENABLED;
  full 39-scenario suite + NeoVisual 190 / Telescope 172; 1 flaky recorded). Includes the
  verify-time fixes for two Gap 1 defects surfaced by the first live e2e execution
  (`w,|` Shift-chord abort; the editor-insert Ctrl+S save oracle). See the Done section.
- **Gap 1 — window-management leader bindings (`w` prefix) + case-sensitive leader
  combos** — GREEN 2026-10-04 (unit-only lane, e2e deferred; gates queued E2E-GAP1-1..5).
  NeoVisual 177 / Telescope 172. See the Done section.
- **Telescope `fzf` finder** — GREEN 2026-10-03 (commit `deebeb2`; bookkeeping `08d31c8`).
   Full 36-scenario e2e + Telescope 172 / NeoVisual 168. See the Done section.
- **Feature 6 — Solution Explorer search-box vim motions (`j`/`k`/`0`/`$`)** — GREEN
  2026-10-04 (commit `d5927e5`; hash recorded `13c7098`). Full 37-scenario e2e + NeoVisual
  171 / Telescope 172. See the Done section.
- **Which-key research** — `docs/plans/whichkey-research.md` (commit `5678b99`).
- **LazyVim gaps 8-11 triage research** — `docs/plans/backlog-plans.md` (annotated
  SUPERSEDED where the user's custom-view decisions override the earlier recommendations).

### Next session — first actions

1. Re-read this handoff + the `[2026-10-03]`/`[2026-10-04]` Decisions entries.
2. Start **Gap 11 (git)** — leader bindings for the native git diff/blame/log/history
   commands (triage EXTEND/REUSE native 2026-10-03; small feature; plan in
   `docs/plans/backlog-plans.md`; **verify the exact command names live** — e2e is
   authorized). Then continue down the run order.
3. The harness-hardening queue items (below) can batch with a future harness touch.

## Current state

- **In progress:** (none — **the columns UX bugfix reached GREEN 2026-10-04** with e2e
  enabled; see the Done section. Nothing is in flight.)
- **Next up:** **the preview buffer-source swap** (`plan-preview-buffer.md` — the fix the
  columns plan's Done entry OWNERS to the planning hub: the workspace buffer
  (`VisualStudioWorkspace` → `GetDocumentIdsWithFilePath` → `GetTextBuffer()`) for solution
  files → FULL Roslyn highlighting (the Peek model); the standalone fallback otherwise;
  CodeLens SKIPPED; the document-window embedding REJECTED. Telescope.Tests 221 → 224; the
  plan is gate-APPROVED in the planning-hub session). **Then:** Gap 11 (the git bindings —
  WRITTEN + APPROVED), Feature 7 (the pane architecture — WRITTEN + APPROVED), Gap 4 (the
  recent-files finder — WRITTEN + APPROVED; `f,e` PROPOSED — confirm at handoff). All plans
  live in
  `.opencode/workspaces/neovim-planning-hub/sessions/neovim-planning-hub-20261004-143017/plans/`
  and are handed off progressively (each written to `docs/implementation_plan.md` when it
  becomes the first item). Then the rest
  of the user-chosen **"smallest first"** run order (2026-10-03):
  gap 11 → feature 7 → gap 4 → gap 5 → gap 6 → gap 10 → gap 9 →
  feature 8 (+gap 8) → feature 9 → which-key last (FEATURE-TRIAGE autonomy granted;
  gaps 8-11 triaged 2026-10-03). The goto commands (Gap 6 core: `gd`/`gI`/`gr` →
  `MyExtension.GotoDefinition/GotoReferences/GotoImplementation`, 1-hit direct / multi-hit
  overlay, the new DefinitionFinder + GotoDispatcher) are **DONE** (GREEN 2026-10-04; see
  the Done section). The Telescope results columns + preview-as-editor item is **DONE**
  (GREEN 2026-10-04; the preview-classifier limitation is documented, fix owned by the
  planning hub). Gap 3 (diagnostics navigation) is **DONE** (GREEN 2026-10-04; see the Done
  section). Gap 1 (window-management leader bindings + case-sensitive leader combos) is
  **DONE** (GREEN 2026-10-04; see the Done section). Feature 6 (Solution Explorer search-box
  vim motions) is **DONE** (GREEN 2026-10-04; see the Done section). The Telescope `fzf`
  finder (item 5) is **DONE** (GREEN 2026-10-03; see the Done section). The Architecture
  backlog is now **CLOSED** — F13 and F43 were verified already-fixed and annotated FIXED
  (2026-10-02). See the Pending queue + User-requested features sections below.

## Decisions (append-only; newest on top)

- [2026-10-04] DECIDED: **goto wiring (user instruction).** The extension exposes **VS
  commands** (`MyExtension.GotoDefinition` / `MyExtension.GotoReferences` /
  `MyExtension.GotoImplementation`); the USER maps `gd`/`gI`/`gr` to them in **VsVim**
  themselves (*"since this is only gonna work in vsvim context text windows its probably
  better that I do it there and just map extension command there — before I just mapped
  default visual studio commands to them and it worked"*). NO leader keys, NO hook changes,
  NO g-sequence state machine, NO VsVim interop. 1 hit → direct jump; 0/multiple hits → the
  Telescope overlay (0-hits → OpenOverlay, LazyVim parity). — status: DONE (GREEN 2026-10-04).
- [2026-10-04] DECIDED: **E2E RE-AUTHORIZED (user instruction).** *"u dont need to defer
  them anymore"* / *"u also have permissions to run e2e now"* — supersedes the earlier
  same-day deferral. The default loop's e2e gates apply again; the queued gates
  E2E-GAP1-1..5 + E2E-GAP3-1..2 were discharged at the Gap 3 VERIFY (full 39-scenario
  suite GREEN, 1 flaky). — status: ACTIVE.
- [2026-10-04] DECIDED: **Gap 3 scope (user instruction).** *"check if there is a way we
  can have ]w [w (for warnings) and [e ]e (for errors) if not try to find a way to create
  that functionality"* — native VS has NO severity-specific commands (researcher-verified;
  the backlog's `Edit.NextError`/`Edit.PreviousError` assumption is NOT verifiable in
  modern VS — FALSIFIED), so: `],d`/`[,d` bind the verified native
  `command:Edit.GotoNextIssueinFile`/`command:Edit.GotoPreviousIssueinFile`, and
  `],e`/`[,e`/`],w`/`[,w` are a BUILT severity-filtered navigator (pure
  `DiagnosticNavigator` + `ErrorListGatherer` + `InputHandler.NavigateDiagnostic`; in-file,
  NO wrap, no-op logged). — status: DONE (GREEN 2026-10-04).
- [2026-10-04] DECIDED: **Gap 1 scope (user instruction).** Window-management leader
  bindings: **skip zoom, skip resize**, **REMOVE the `Space+W` save binding** (the user
  saves with Ctrl+S), `w` becomes the **window-management prefix** (`w,-` split below via
  `command:Window.NewHorizontalTabGroup`, `w,|` split right via
  `command:Window.NewVerticalTabGroup`, `w,d` focus-aware `close-window`), and leader
  combos are **CASE-SENSITIVE** (a capital letter in the config = Shift+letter; `s,g` ≠
  `s,G`). Executed in the unit-only lane (e2e deferred; gates queued E2E-GAP1-1..5). —
  status: DONE (GREEN 2026-10-04).
- [2026-10-04] DECIDED: **E2E TESTS DEFERRED (user instruction).** Do NOT run the e2e
  harness (`tools/harness/test-e2e.ps1`, incl. `-Tests <subset>`) until the user explicitly
  says so. All items from now on use the **unit-only lane**: RED at the unit level only;
  e2e gates are **queued in `docs/e2e-queue.md`** (status QUEUED) and run later on a
  VS-capable machine. The no-VS harness-health self-checks (`-List`, `-SelfCheck`,
  `check-doc-refs.ps1`) remain allowed. VERIFY runs the affected unit project(s); the final
  gate runs both unit projects (no e2e). — status: ACTIVE.
- [2026-10-04] DECIDED: **New planning tactic — two-stage fan-out (user instruction).**
  Stage 1: one `implementation-planner` produces a **general plan/architecture**
  (section-structured, NOT BP-n). Stage 2: the **hub** splits it into sections and
  dispatches **multiple parallel** `implementation-planner` tasks (one per section). Stage 3:
  the **hub** aggregates the sections into the final `docs/implementation_plan.md` (Build
  Plan + Verification Trace). Reason: the single-planner approach returned empty results
  twice on Gap 1; fan-out is more robust and parallelizes detail work. — status: ACTIVE.
- [2026-10-04] DECIDED: **Build loop PAUSED by the user** after Feature 6 GREEN; resume at
  **Gap 1** using the two-stage fan-out tactic. See the SESSION HANDOFF block at the top of
  this file. — status: ACTIVE.
- [2026-10-03] DECIDED: LazyVim gaps 8-11 triage (via the `question` tool) — **Gap 8
  (quickfix)** = use the native Error List data but display it in a **custom
  Telescope-style view**; also fold quick-fix actions into the **code-actions picker**
  (feature 8): `Leader+C+A` must show all possible code actions for the current
  selection/at-caret, with **quick-fix actions at the top** and the **warning-fix action
  first**. **Gap 9 (search/replace)** = extend/reuse native replace semantics but build a
  **custom overlay**: two input fields (search, replace); seed the search from the current
  selection, or the word at the caret if there is no selection; navigate between hits;
  replace the current hit; replace all. **Gap 10 (hover/signature)** = extend/reuse native
  QuickInfo/ParameterInfo data but build a **custom focusable overlay** navigable with vim
  motions (to read docs/help/notes). **Gap 11 (git)** = **extend/reuse native** — add
  leader bindings for the native git diff/blame/log/history commands (verify exact command
  names live). Gaps 8/9/10 are **custom-view builds** (feature lane); gap 11 is **native
  bindings** (small); gap 8 is **folded into feature 8**. Chosen run order (**smallest
  first**): feature 6 → gap 1 → gap 3 → gap 11 → feature 7 → gap 4 → gap 5 → gap 6 →
  gap 10 → gap 9 → feature 8 (+gap 8) → feature 9 → which-key last. **Which-key decision:**
  at the very end, build **BOTH** a bottom-right non-focus-stealing popup overlay
  (LazyVim-style) AND an inline popup variant, so both can be tested. Plans in
  `docs/plans/backlog-plans.md` / `docs/plans/whichkey-research.md` — status: PLANNED
  (not executed).
- [2026-09-28] DECIDED: Telescope `fzf` finder scope (via the `question` tool) — the
  user wants **BOTH** the fuzzy content finder (A: a `FzfFinder` scanning file contents
  with fzf fuzzy matching, preview + open at line) AND the fuzzy file finder (B: the
  file-name finder filtered by fzf — the existing `FileFinder` already does this;
  confirm/keep it). Plan in `docs/plans/backlog-plans.md` — status: PLANNED (not executed).
- [2026-09-28] DECIDED: LazyVim gap-analysis triage (via the `question` tool) — Gap 1
  window mgmt = EXTEND/REUSE native (split/zoom/delete-window via native commands); Gap 2
  buffer switch = SKIP (VsVim Shift+H/J/K/L already handles it); Gap 3 diag nav =
  EXTEND/REUSE native (Edit.NextError/PreviousError); Gap 4 recent files = BUILD
  (Telescope-style finder); Gap 5 symbols finder = BUILD (Telescope-style); Gap 6 goto
  def = BUILD (goto-definition finder + wire VsVim gd/gr/gi to the Telescope finders);
  Gap 7 org imports = SKIP. Gaps 8-11 (quickfix, search/replace, hover/signature, git
  status/diff/blame/log) NOT YET TRIAGED. Chosen items added to the pending queue
  (items 6-10). Plans in `docs/plans/backlog-plans.md` — status: PLANNED (not executed).
  **SUPERSEDED 2026-10-03:** gaps 8-11 are now triaged — see the newest Decisions entry
  (gap 8 folded into feature 8; gaps 9/10 custom overlays; gap 11 native bindings).
- [2026-09-28] DECIDED: FEATURE-TRIAGE for user-requested features 6-9 — the user
  chose **BUILD** for all four (via the `question` tool): (6) extend the Solution
  Explorer search-box vim motions to j/k/0/$ + block caret; (7) extend the overlay to a
  3-way Input/List/Preview pane switch with Ctrl+J/K; (8) build a Telescope-style
  code-actions picker (`Leader+C+A`); (9) build a Telescope-like vim-mode overlay for
  Solution Explorer rename/move/add. Each is a feature-lane item (M-M7 applies to any
  that add/change a `[Telescope]`/`[NeoVisual]` diagnostic). Plans in
  `docs/plans/backlog-plans.md` — status: PLANNED (not executed; needs a VS-capable machine).
- [2026-09-28] DECIDED: Trailmark guidance is single-sourced at
  `.opencode/agent/trailmark-guidance.md` (all agents reference it, do not re-derive)
  — reason: the copies had drifted twice (W3/W18) — status: ACTIVE.
- [2026-09-28] DECIDED: `-NoBootstrap` reuse is allowed ONLY for the flaky-retry
  re-run; the final-gate full suite always boots fresh — reason: reuse mode
  re-snapshots the scratch as the `seed-leak` baseline, masking a seed write from an
  earlier run — status: ACTIVE.
- [2026-09-28] DECIDED: `log/tools-hash.txt` is allowlisted in `check-doc-refs.ps1`
  — reason: absent on a fresh clone is NOT drift; existence is enforced by
  `Write-ToolsHash` at every real run — status: ACTIVE.
- [2026-09-27] DECIDED: `subagent_depth` stays at the intended value 2 (a local
  config may differ temporarily) — reason: nested `trailmark-recon`/`code-slice-worker`
  spawns need depth ≥ 2; do not lower to 1 or raise to ≥3 — status: ACTIVE.
- [2026-09-27] DECIDED: model pins removed from all agents (inherit session default)
  — reason: commit `4a2ec0b`; the "cheaper model" rationale for nested recon is void
  — status: SUPERSEDED (M-M4).

## Baseline (as of last full verification)

- Offline units: `tests/Telescope.Tests` **221 passed**; `tests/NeoVisual.Tests`
  **190 passed** (after the columns UX bugfix, 2026-10-04).
- Live E2E: `tools/harness/test-e2e.ps1` lists **41 scenarios** — ALL executed GREEN
  (full-suite fresh boot with `-TimeoutSec 2400`, run 169, 2026-10-04; the columns UX
  bugfix's final gate — `telescope-results-columns` first-try, the diagnostics byte-stable,
  zero flakes). **No known-RED remains** —
  `explorer-open-searchbox` was GREened 2026-09-27. Both former allowlist entries are now FIXED (`neovisual-editor-insert` d18315,
  `telescope-implementation` 7c6569b) - the full 41-scenario suite is GREEN.

## Known bug backlog (from previous session, run 55)

> **ALL FOUR ITEMS FIXED + VERIFIED GREEN on 2026-09-19** (see the Done section
> below). Kept for historical reference.

1. **`telescope-prompt-motions` — wrong expected caret for `e`.** ✅ FIXED
   (harness assertion corrected to `key=E caret=4` + extra `w` tap). The scenario
   asserted `prompt-motion key=E caret=5` but actual was `caret=4`. EndWord math
   was wrong: on `find my file`, EndWord from 0 stops at `i=3` (last char 'd')
   then `MoveTo(i+1)` = **4**, not 5. Correct expected sequence after the three
   `b` taps (caret → 0):
   - `e` → `key=E caret=4`
   - `w` from 4 → `key=W caret=5`
   - `w` from 5 → `key=W caret=8`
   - `w` from 8 → `key=W caret=12`
   - `0` → `key=D0 caret=0`
   - `$` → `key=D4 caret=12`
   (Earlier h=11, l=12, b=8, b=5, b=0 are correct.)

2. **`telescope-open-file-normal` — wrong key name in assertion.** ✅ FIXED
   (assertion corrected to `key=Return mode=normal handled=True`). The scenario
   asserted `key=Enter mode=normal handled=True`, but WPF `Key` for Enter is
   `Key.Return`. (The file DID open — only the log-name assertion was wrong.)
   `telescope-open-file` (insert) does not rely on a `key=Enter` line.

3. **`telescope-issues` — Error List noise breaks `results count=1`.** ✅ FIXED
   (assertion relaxed to `results count=\d+ selected=0`). VS Error
   List accumulates warnings/errors during the session, so `results count` is
   non-deterministic. The seeded TODO is still ranked first; the
   `preview file=...TodoProbe.cs`, `preview caret=... line=1`,
   and `opened issue: ...TodoProbe.cs line=...` assertions prove the
   right issue is selected/opened.

4. **`neovisual-explorer-open` + `neovisual-explorer-open-o` — "Enter storm".** ✅ FIXED
   (extension bug, architecture-review F1). The injected `Return`
   was re-captured by the global hook (Enter is an action key → `IsInteresting`
   true), re-routed to `SolutionExplorerController.OpenSelected()`, which injected
   another `Return` → infinite re-injection storm (`solution-explorer open` fired
   ~30x in ~100ms; observed 93/62 lines vs the new ≤10 harness fail-fast bound).
   Fix: `InjectedKeyGuard` (per-VK consume-once counter) recorded in
   `KeyInjection.Press`; `GlobalKeyboardHook.HookCallback` passes matching
   key-downs through with `CallNextHookEx` (never re-handles/re-injects).
   `neovisual-explorer-open-o`'s focus flakiness also fixed (F16:
   `Assert-VsFocused` in the walk loop).

### New planned E2E scenarios (user-requested) — status 2026-09-19

- ✅ **`telescope-open-file-searchbox`** — DONE (see Done section): type a query in
  insert mode, wait for the settle, Enter opens the filtered result.
- ✅ **`telescope-open-file-navigation`** — DONE (see Done section): Esc to normal,
  **j moves the selection to index 1**, Enter opens the moved-to row (`Service.cs`).
- ✅ **`explorer-open-navigation`** — **GREEN 2026-09-19** (tree-select capability, `g`;
  see Done section). Historical note: it was KNOWN-RED here until the `g`
  programmatic select-first-source-file action shipped. (`neovisual-explorer-open`
  remains the separate Enter-storm scenario — that one is green too.)
- ✅ **`explorer-open-searchbox`** — **GREened 2026-09-27** (historical: was KNOWN-RED
  — after `i`→type→`Esc`, `ExitInputMode`'s `View.SolutionExplorer` refocus did NOT
  restore tree focus, so `o` fell through into the search box; fixed by
  `ReturnFocusToTree` + `HierarchyResolver.FirstPathMatching`; see the Done section).
  **No known-RED remains.**

> Harness-health note (2026-09-19): the two test projects compile the shared
> `Telescope.csproj` into the same `obj/` path; Defender AV occasionally locks
> `Telescope.dll` (CS2012) on a simultaneous launch. A re-run passes; not a test
> failure. **Policy now staggered/sequential** (W11, `verification-agent.md` step 4)
> — do not launch both unit suites simultaneously.

## In-progress

- (none — Gap 3 reached GREEN 2026-10-04 with e2e enabled and
  `explorer-open-searchbox` reached GREEN 2026-09-27; see the Done section. No
  known-RED remains.)

## F45 status (Item 1 — log-prefix centralization)

**COMPLETE** — code and e2e verified (see the Done section). `Run_LogPrefixes_Pinned`
passes, all rg gates pass at the time of F45 (C# literals = 0; harness
raw literals = 1 each = the `$script:Pfx*` definitions; Pfx usage = 102 = exact
expected 91+4 + 3+4), and the 19-scenario e2e subset ran green (the only 2 failures
were known-backlog assertion bugs, not regressions).

## Known bug backlog (added 2026-09-27, while fixing `explorer-open-searchbox`)

> **USER-REPORTED (user saw a real leak).** Both items below were found during the
> `explorer-open-searchbox` item and are **not** accessory — they are real harness/teardown
> defects. Fix them promptly.

1. **`neovisual-explorer-move` leaks the `m` key straight into the editor.**
   `SolutionExplorerController`'s `m` action injects the VS Move command with NO focus guard:
   when Solution Explorer is NOT focused (a document holds focus), the physical `m` falls
   through to the editor and is typed (in VsVim normal mode `m` starts a motion → `ljoljoljo`
   becomes a typed storm). Reproduced in a per-run `log/` file (15:30:29.338 `m` →
   `solution-explorer move`, then LMenu; the run's forced teardown saved 20s later). The
   user observed `ljoljoljoljoljo` typed into a seeded file (likely `Models/Order.cs`, the
   first file in the loop iteration order) from the **explorer-navigation** storm — the same
   class of defect. **Fix direction:** only act (and only consume the key) when the Solution
   Explorer tree is the WPF-focused element (the controller already has `TextMotionHelper`
   focus probes); otherwise fall through / swallow. Also stop the harness's
   `Assert-NoEnterStorm`/teardown from happening mid-scenario (the 20s LMenu gap proves the
   teardown fired while a scenario was still running — see #2).
> **STATUS 2026-09-27: FIXED** — the `FocusGuard` (`EditorFocusedVeto`,
> `MyExtension/ToolWindows/FocusGuard.cs`) closes this leak; see the Done entry
> (commit `1f32d00`).

2. **Harness teardown force-killed VS without saving → next run gets the "did not close
   properly / unsaved changes" prompt, and the leak evidence is lost.** ✅ **FIXED 2026-09-27**
   (part of this item): `tools/harness/test-e2e.ps1` now has `Save-AllDocuments` (resolves the
   instance's DTE from the ROT by PID via `dte-command.ps1` and runs `File.SaveAll`) and calls
   it on EVERY kill path — `Stop-SpawnedVs`, `Stop-HarnessVs`, and the stale-exploration
   cleanup — so open documents are flushed to disk *before* the process is stopped. This both
    preserves a leak's file content for inspection and prevents the next run's clean-close
    warning.

## Pending queue (next items to pick)

Top of the queue, in priority order:

> **RUN ORDER (user-chosen 2026-10-03, "smallest first"):** ~~feature 6~~ (**DONE** GREEN
> 2026-10-04) → ~~gap 1~~ (**DONE** GREEN 2026-10-04) → ~~gap 3~~ (**DONE** GREEN
> 2026-10-04) → **gap 11 (next)** →
> feature 7 → gap 4 → gap 5 → gap 6 → gap 10 → gap 9 → feature 8 (+gap 8) →
> feature 9 → which-key last. Gaps 8/9/10 are **custom-view builds** (feature lane); gap 11
> is **native bindings** (small); gap 8 is **folded into feature 8**. The which-key item is
> built **last** and delivers **BOTH** a bottom-right non-focus-stealing popup overlay
> (LazyVim-style) AND an inline popup variant, so both can be tested.

> ~~**FIRST ITEM (2026-10-04):** the **columns UX bugfix** plan~~ — **DONE** (GREEN
> 2026-10-04, bugfix + UX lane, e2e ENABLED; see the Done section). Full 41-scenario
> fresh-boot suite GREEN (run 169, zero flakes); Telescope.Tests 208 → **221**; NeoVisual
> 190. Shipped: per-column min/max widths (the pinned 11-row table across all 23 catalog
> sites), the pure `ColumnWidths.Compute` (the priority distribution + the exact-total
> invariant + the degenerate mins-win case), **logical shortening** (Tail = path-like
> columns truncate from the FRONT — the end folder + file name survive; End = text), the
> **overlay width scales with the visible column count**
> (`min(max(760, NeededWidth+18+480+22), WorkArea.Width)`, recomputed at open + every chooser
> toggle), the h-scrollbar Disabled, the **selection contrast** (the IsSelected trigger sets
> a contrasting foreground, active + inactive). No new diagnostic (the existing
> `results columns=` / `results count=` lines byte-stable).

> **THEN (2026-10-04, gate-APPROVED, handed off in order):**
> 2. **the preview buffer-source swap** (`plan-preview-buffer.md`) — the fix the columns plan's
>    Done entry OWNERS to the planning hub: the workspace buffer (`VisualStudioWorkspace` →
>    `GetDocumentIdsWithFilePath` → `GetTextBuffer()`) for solution files → FULL Roslyn
>    highlighting (the Peek model); the standalone fallback otherwise; CodeLens SKIPPED (the
>    References finder covers counts); the document-window embedding REJECTED (unsupported).
>    Telescope.Tests 212 → 215.
> 3. **Gap 11 — the git bindings** (`plan-gap11.md`): `g,d` diff / `g,b` blame / `g,h` history
>    (the branches binding DROPPED — the user's decision; the deferred lazygit overlay covers
>    it); `g,g`/`g,c` unchanged until that overlay ships; the scratch repo seeded (`git init`,
>    `.git` excluded from the seed-leak set); the e2e scenario `neovisual-git-bindings`.
> 4. **Feature 7 — the overlay PANE architecture** (`plan-feature7.md`): the user's directive —
>    REAL focus (not logical), left-click focusable panes, one modular overlay (an IPane
>    contract + a PaneHost registry — the types land with the plan) the deferred lazygit
>    overlay reuses; Ctrl+H=List, Ctrl+L=Preview, Ctrl+J=Input,
>    Ctrl+K=cycle (the wrap pinned); M-M7 (`focus target=Input|List|Preview` — the 4 harness
>    sites update); the e2e scenario `telescope-focus-panes`.
> 5. **Gap 4 — the recent-files finder** (`plan-gap4.md`): `Name="Recent"`, the leader `f,e`
>    (PROPOSED — confirm at handoff); CRITICAL: `EnvDTE.RecentFiles` does NOT exist in the 17.x
>    interop → the reflection probe + the session-MRU fallback behind the unchanged seam; the
>    e2e scenario `telescope-recent`.
> The lazygit overlay (`g,g`) remains DEFERRED (a bonus when the core is finished — Feature 7's
> pane host is its foundation). Gap 5 (symbols) is the next planning batch.

> **FIRST ITEM (2026-10-04):** ~~the **Telescope results columns + preview-as-editor**
> plan~~ — **DONE** (GREEN 2026-10-04, feature lane, e2e ENABLED; see the Done section).
> Full e2e lane executed: the scenario `telescope-results-columns` was CREATED + PROVEN RED
> (the missing `results columns=` contract) before the build, then the full 40-scenario
> suite ran GREEN at VERIFY (1 flaky: `neovisual-window-management` pass-on-retry —
> cumulative flaky count 2, hardening item a). Shipped: the results TextBox → a real
> ListView+GridView (23 catalog columns, the user's marks default-ON, right-click header
> chooser, abbreviated values W/R + err/warn + imp/func/inf…, the NEW
> `[Telescope] results columns={ids}` diagnostic, the byte-stable `results count=` form);
> the preview RichTextBox + custom SyntaxHighlighter RETIRED → a real read-only VS editor
> view (no VsVim insert — the `Editable` role excluded). **KNOWN LIMITATION (documented,
> fix OWNED BY THE PLANNING HUB — in flight):** the workspace-detached preview buffer gets
> NO Roslyn C# classifier → `preview tokens=0` (no semantic highlighting); the harness
> asserts the line's PRESENCE only until the workspace-attach fix lands. Telescope.Tests
> 172 → 199; NeoVisual 190.

> **SECOND ITEM (2026-10-04):** ~~the **goto commands** plan~~ — **DONE** (GREEN 2026-10-04,
> feature lane, e2e ENABLED; see the Done section). Three VS commands —
> `MyExtension.GotoDefinition` / `MyExtension.GotoReferences` / `MyExtension.GotoImplementation`
> (a NEW `MyExtensionPackage.vsct` — none existed): 1 hit → jump directly, 0/multiple hits →
> the Telescope overlay with the corresponding finder; a NEW `DefinitionFinder` (Roslyn
> `DeclaringSyntaxReferences`, finder-side deterministic ordering); the pure `GotoDispatcher`
> seam in `Telescope/Controller/`; the USER maps `gd`/`gI`/`gr` to the commands in VsVim
> themselves (their explicit wiring decision — no hook/leader changes). New e2e scenario
> `telescope-goto` (41st) — created + PROVEN RED (DTE unknown-command rejection) before the
> build, PASSED its first live run after the Part-4 caret-normalization fix. Telescope.Tests
> 199 → 208; NeoVisual 190 (registry 16 → 17). The 3rd-strike `neovisual-window-management`
> regression was FIXED in this item (step-0 editor-focus establishment — hardening item a
> executed).

> **HARDENING QUEUE (harness, filed 2026-10-04 from the Gap 3 VERIFY — batch with a future
> harness touch; none blocks the queue):**
> a. ~~**`neovisual-window-management` step-3 order-dependency (flaky ×2 cumulative — ONE more
>    flake triggers the 3rd-strike REGRESSION upgrade).**~~ **FIXED 2026-10-04** (the goto
>    item's verify-time debug — the 3rd strike fired in runs 165/166 and was upgraded to a
>    REGRESSION per the M-M2 budget): the scenario now ESTABLISHES editor focus at start
>    (step 0 opens Alpha.cs via the overlay + `Wait-ActiveDocumentMatch`) before the binding
>    steps; run 167 (subset) + run 168 (full suite) PASS. Root cause: the scenario asserted
>    VS-process foreground only, so with a tool window focused the doc-window commands were
>    unavailable and the focus-aware `w,d` routed on the stale `IsToolWindow` flag.
> b. **Cleanup `Save-AllDocuments` writes seeds AFTER the `seed-leak` check** — the guard's
>    window excludes end-of-run cleanup writes (harmless across boots — each boot reseeds —
>    but a coverage gap). Fix direction: run the leak check after the final save, or save
>    before the check.
> c. **`Ensure-SolutionExplorerOpen` vs an input-mode tool-window controller** — the StartPage
>    (a text-input-type tool window) starts in input mode and silently swallows Space+E; the
>    Gap 3 debug added `Enter-NormalContext` to `neovisual-window-management` step 4 only —
>    the helper's other 11 callers share the latent state. Fix direction: hammer Escape (or
>    exit-input) inside `Ensure-SolutionExplorerOpen` itself.
> d. **Documented, no action:** the tab-group DTE commands (`Window.NewHorizontalTabGroup`/
>    `NewVerticalTabGroup`) log `Command ... is not available` on a fresh instance — the
>    scenarios' contract is the `leader-binding executed:` line, not command success
>    (documented at `test-e2e.ps1:718-720`). (Also: with editor focus restored,
>    `w,-` creates a horizontal group after which VS natively disables
>    `Window.NewVerticalTabGroup` — horizontal+vertical groups cannot mix; the scenario
>    asserts the binding diagnostic only.)
> e. **The harness's DEFAULT suite budget (300s) cannot fit 41 scenarios** — run 165 expired
>    at 37 scenarios and suppressed the FAIL summary; the full-suite gate REQUIRES an explicit
>    `-TimeoutSec 2400` (documented in the goto plan's Verification Trace). Fix direction:
>    raise the default (scale by scenario count) or fail fast with a clear message.

> **KNOWN LIMITATION (2026-10-04, user-owned — the fix is IN FLIGHT IN THE PLANNING HUB; do
> not pick this up as a queue item):** the preview's workspace-detached buffer
> (`ITextDocumentFactoryService.CreateAndLoadTextDocument`) gets **NO Roslyn C# classifier**
> → `preview tokens=0` and no semantic highlighting in the hosted read-only editor preview
> (the plan's D9 caveat realized). The harness asserts the line's PRESENCE only
> (`preview tokens=\d+`) until the workspace-attach fix lands; spec §4 + AGENTS.md document
> the limitation in place.

> **FIRST ITEM (2026-10-04):** ~~the **Gap 3 — diagnostics navigation (`]`/`[` prefix)**
> plan~~ — **DONE** (GREEN 2026-10-04, feature lane, **e2e ENABLED**; see the Done section).
> Executed A → B → C → D → E in one pass (the mid-plan inert-state expectations honored);
> the full-suite gate then surfaced two REAL defects in Gap 1's queued e2e gates (both
> fail-twice) — fixed at verify-time debug (plan Phase 7, BP-H1..H3): the leader matcher now
> treats modifier keys as transparent while a sequence is active (`w,|` fired for the first
> time in 155 runs), and `neovisual-editor-insert`'s save step uses the DTE
> `Save-AllDocuments` (the injected Ctrl+S chord is consumed by the focused editor's
> key-processing chain). Re-VERIFY: full 39-scenario fresh-boot suite GREEN (1 flaky:
> `neovisual-window-management` pass-on-retry — hardening item a above); NeoVisual 190 /
> Telescope 172. The queued gates **E2E-GAP1-1..5 + E2E-GAP3-1..2 are discharged** (removed
> from `docs/e2e-queue.md` per its run-GREEN rule). Scope shipped per the user (2026-10-04):
> `],d`/`[,d` → native `command:Edit.GotoNextIssueinFile`/`command:Edit.GotoPreviousIssueinFile`
> (the backlog's `Edit.NextError`/`Edit.PreviousError` assumption was FALSIFIED);
> `],e`/`[,e`/`],w`/`[,w` → the BUILT severity-filtered navigator (in-file, NO wrap, no-op
> logged).

> **FIRST ITEM (2026-10-04):** ~~the **Gap 1 — window-management leader bindings (`w` prefix)
> + case-sensitive leader combos** plan~~ — **DONE** (GREEN 2026-10-04, feature lane,
> unit-only; see the Done section). Executed A → B → C → D → E in one pass per the plan's
> binding execution-order warning; the documented transient-RED set was owned by its Phase 3
> (not regressions). e2e gates **QUEUED: E2E-GAP1-1..5** (`docs/e2e-queue.md`) — run them on
> a VS-capable machine when the user says so. The known pre-existing DOC-66-3 lint failure
> was reconciled as part of BP-E7 (the lint expectation in `tools/lint/check-doc-content.ps1`
> now accepts the current Baseline attribution; lint PASS 12/12). Scope shipped per the user
> (2026-10-04): zoom/resize skipped, `Space+W` save removed, `w` window prefix (`w,-`/`w,|`/
> `w,d` focus-aware `close-window`), leader combos CASE-SENSITIVE, no new diagnostic literal.

> **FIRST ITEM (2026-10-02):** ~~the **Code review fixes (72 findings)** plan~~ —
> **DONE** (GREEN 2026-10-02, bugfix lane, **e2e ENABLED**; see the Done section). Source:
> `docs/reviews/code-review.md` (2026-10-02, 72 findings: 2 critical, 8 major, 44 minor,
> 18 nit — the user requested fixes for ALL findings, incl. minors/nits). Plan in
> `docs/implementation_plan.md` (8 phases: 0-7 code-review fixes; 70 BP steps + Verification
> Trace). Full 35-scenario e2e suite + both unit suites GREEN at VERIFY. The plan does NOT
> subsume the still-open Architecture backlog findings (F13/F43 stay in the backlog —
> no-seam/not-RED-provable).

> **FIRST ITEM (2026-10-02):** ~~the **Code review fixes (51 findings)** plan~~ —
> **DONE** (GREEN 2026-10-02, unit-only lane; see the Done section). Source:
> `docs/reviews/code-review.md` (2026-10-02, 51 findings:
> 0 critical, 5 major, 22 minor, 24 nit — the user requested fixes for ALL findings,
> incl. minors/nits). Plan in `docs/implementation_plan.md` (10 phases: 0-9 code-review
> fixes; 51 BP steps + Verification Trace).
> **DEFER e2e tests (unit-only lane):** e2e scenarios are queued in `e2e-queue.md`
> (E2E-CR51-1..10, status QUEUED) — run them on a VS-capable machine after this plan is
> GREEN. The plan subsumes Architecture backlog findings F5→R3, F7→R4, F8/F9→R22/R42,
> F10→R2, F14→R45 (documented/accepted n16) — do not double-execute those. It also
> re-fixes the prior regressions M1→R1, M4→R2, M7→R24.
> **R45 disposition (BP-43, recorded 2026-10-02):** the Ctrl+N/P unconditional swallow in
> `PopupNavigation` (PopupNavigation.cs:47-61) is the documented/accepted n16 design —
> re-verified still live; NO code change (keep documented/accepted).

> **FIRST ITEM (2026-10-01):** ~~the **Code review findings (98 findings) + Functional
> restructure** combined plan~~ — **DONE** (GREEN 2026-10-02, unit-only lane; see the Done
> section). Source:
> `docs/reviews/code-review.md` (2026-10-01, 98 findings: 0 critical, 8 major, 69 minor, 21 nit)
> + a user-requested functional restructure (main files separated from helpers/utils by
> functionality — a `Utils/` subfolder per area — plus 5 renames: WindowMatrix→WindowNavigator,
> CardinalNavigationConstants→NavigationConstants, UtilityMethods→WindowFrameUtils,
> RectCoordinate→WindowRect, WindowAdapter→WindowFrameAdapter; namespaces unchanged; user
> decisions 2026-10-01). Plan in `docs/implementation_plan.md` (15 phases: 0-10 code-review
> fixes, 11-14 restructure; 76 BP steps + Verification Trace).
> **DEFER e2e tests (unit-only lane):** e2e scenarios are queued in `e2e-queue.md`
> (E2E-NCR-1..2, E2E-NCR-M2/M4/M5/M3/M7/m47, E2E-RESTRUCTURE-2, E2E-NCR-BACKLOG, status
> QUEUED) — run them on a VS-capable machine after this plan is GREEN. The plan does NOT
> subsume the still-open Architecture backlog findings (F13/F14/F43 stay in the backlog —
> no-seam/not-RED-provable). The plan's Phase 10 reconciles the prior 67-findings plan's
> missing e2e gates (E2E-NCR-BACKLOG — the gates `docs/progress.md` claimed were queued in
> `e2e-queue.md` but never materialized; now appended 2026-10-01).

> **FIRST ITEM (2026-09-30):** ~~the **Code review findings (67 findings) + Repository
> restructure** combined plan~~ — **DONE** (GREEN 2026-09-30, unit-only lane; see the Done
> section). Source:
> `docs/reviews/code-review.md` (2026-09-30, 67 findings: 2 critical, 31 major, 32 minor, 2 nit)
> + a user-requested repo restructure (folders + namespaces + tools + docs; user decisions
> 2026-09-30: folders+namespaces, one combined plan, source+tools+docs scope). Plan in
> `docs/implementation_plan.md` (14 phases: 0-10 code-review fixes, 11-14 restructure).
> **DEFER e2e tests (unit-only lane):** e2e scenarios are queued in `e2e-queue.md`
> (E2E-NCR-1..2, E2E-NCR-M1/M2/M15 .. E2E-NCR-M26/M27/M28, E2E-RESTRUCTURE-1, status
> QUEUED) — run them on a VS-capable machine after this plan is GREEN. The plan subsumes
> Architecture backlog findings F2→M1, F5→CR2, F7→M9, F8/F9→M2, F20→M24, F22→M18,
> F23→M15 — do not double-execute those. Restructure deviations (user-approved): the
> `MyExtension/Navigation/` folder → `Navigation/` + `MyExtension.Navigation` (AGENTS.md typo
> rule superseded by the restructure); operational docs (spec.md, progress.md,
> implementation_plan.md, e2e-queue.md) stay in `docs/` root.

> **FIRST ITEM (2026-09-29):** ~~the **Code review findings (75 findings, 12 phases)**
> plan~~ — **DONE** (GREEN 2026-09-30, unit-only lane; see the Done section). All 12
> phases executed per-phase RED→GREEN; final suites Telescope.Tests 135 / NeoVisual.Tests
> 130 (both 0 failed); `dotnet build` 0 errors; all harness-health self-checks PASS. The
> e2e gates are queued in `e2e-queue.md` (E2E-CR-1..3, E2E-M1..M42, status QUEUED) — run
> them on a VS-capable machine (the correct machine runs VS 2026) before relying on live
> behavior. No e2e harness commands were run on this machine (user mandate).

> **FIRST ITEM (2026-09-28):** ~~the **Architecture consolidation (5 lanes)** plan~~ —
> **DONE** (GREEN 2026-09-28, unit-only lane; see the Done section). The e2e gates are
> queued in `e2e-queue.md` (E2E-AC-1..5, status QUEUED) — run them on a capable machine
> before relying on live behavior. The consolidation subsumed backlog findings
> (Architecture F2,F3,F4,F6,F7,F11,F36,F37,F38,F39,F40,F41,F44,F46; Code
> M4,M5,M8,M9 + m17,m18,m19,m21,m23,m25,m33,m37,m38,m43,m44,m45; nits n2,n3) — do not
> double-execute those. NOT subsumed (stay in the backlog): F5,F8,F9,F12,F13,F14,F15,
> F22,F43,M19,m6,m7,m8,m9,m10,m11,m12,m13,m16,m20,m22,m24,m32,m34,m35,m36,m46,m47,m48,
> m58,m59,m60,m62,m63,m64,m65,n1,n4-n16.

> **Next candidates (after the consolidation plan):** the still-open **Architecture
> review backlog** findings NOT subsumed by the consolidation (F5, F8, F9, F12, F13,
> F14, F15, F22, F43 — filed 2026-09-19, no FIXED annotation) are the next items for
> the build loop; pick them before the user-requested features. **UPDATE 2026-09-30:**
> F5, F8, F9, F22 are now covered by the combined plan (F5→CR2, F8/F9→M2, F22→M18) and
> F12/F15 are already FIXED (2026-09-28) — do not double-execute. Remaining after the
> combined plan: F13 (no-seam dedup), F14 (no-seam VS-coupled), F43 (not RED-provable).
> **UPDATE 2026-10-02:** F5→R3, F7→R4, F8/F9→R22/R42, F10→R2, F14→R45 are now covered
> by the 51-findings plan (GREEN 2026-10-02) — do not double-execute. Remaining
> after it: F13 (no-seam dedup), F43 (not RED-provable).
> The Telescope `fzf` finder (item 5) — **DONE** (GREEN 2026-10-03; see the Done section).
> The queue's next item is the user-requested features 6-9 (LazyVim gaps).

0. ~~Harness seeding hardening~~ — **DONE** (see Done section).
1. ~~F45 e2e verification subset (BP-23)~~ — **DONE**: the 19-scenario gate ran
   green; the only 2 failures were known-backlog assertion bugs (`prompt-motions`
   caret, `open-file-normal` key name), not F45 regressions. F45 is complete.
2. ~~Fix the 4 known-bug items (test-harness fixes + the Enter-storm
   investigation)~~ — **DONE** at the time (26-scenario suite GREEN; units were
   25/42 then — both counts have since grown; see the Baseline section); see the Done
   section.
3. Implement the **Telescope finders** roadmap:
   - ~~`references` finder~~ — **DONE** (see Done section; `Space+F R`,
     read/write access, preview line-jump).
   - ~~`grep` finder~~ — **DONE** (see Done section; `Space+F G`, query-driven
     with a debounce, preview line-jump).
   - ~~`implementation` finder~~ — **DONE** (see Done section; `Space+F I`,
     Roslyn `FindImplementationsAsync`, preview line-jump).
   - ~~`fzf` finder — with preview pane.~~ — ✅ **DONE** (see the Done section;
     `Space+F Z`, `FzfFinder` fuzzy content finder + the existing `FileFinder` as the
     fuzzy file finder; new e2e scenario `telescope-fzf`).
4. ~~Add the 4 new planned E2E scenarios~~ — **PARTIAL**: `telescope-open-file-searchbox`
   + `telescope-open-file-navigation` **DONE** (see Done section); the other 2
   exposed real gaps → now the next queue items:
   - ~~**`explorer-open-navigation`**~~ — ✅ **DONE** (tree-select capability,
     GREEN 2026-09-19; see Done section).
   - ~~**`explorer-open-searchbox`**~~ — ✅ **DONE** (search-box focus-exit gap
     fixed, GREEN 2026-09-27; see Done section).
5. ~~**Telescope `fzf` finder**~~ — ✅ **DONE** (see the Done section; `Space+F Z`,
   `FzfFinder` fuzzy content finder + the existing `FileFinder` as the fuzzy file finder;
   new e2e scenario `telescope-fzf`; diagnostics `fzf hits=...`,
   `fzf unavailable — literal fallback`, `opened fzf: file=... line=...`).
5.5. ~~**`telescope-implementation` — intermittent injected-Enter loss.**~~ ✅ **DONE
    2026-09-27** (see the Done section; commit `7c6569b`). Root cause was a **harness focus
    race** — `Assert-OverlayFocused` was PID-only, so Enter was injected before the overlay
    became the OS foreground window. Fixed by requiring the actual overlay window (PID +
    title `Telescope`); `telescope-implementation` now passes 3/3 sequential runs. (The
    original "no `[Hook] key=Return`" clue was a red herring — `Keys.Return` is not in
    `IsInteresting`; the real line is `[Telescope] key=Return … handled=True`.)
6. ~~**Gap 1: Window management**~~ — **DONE** (GREEN 2026-10-04, unit-only; see the Done
   section): `w,-` split below, `w,|` split right, `w,d` focus-aware `close-window`;
   zoom/resize skipped per the user; `Space+W` save removed; leader combos case-sensitive.
   e2e gates ran GREEN at the Gap 3 VERIFY (E2E-GAP1-1..5 discharged).
7. ~~**Gap 3: Diagnostics navigation**~~ — **DONE** (GREEN 2026-10-04, e2e enabled; see the
   Done section): `],d`/`[,d` → native `command:Edit.GotoNextIssueinFile`/
   `command:Edit.GotoPreviousIssueinFile` (the `Edit.NextError`/`Edit.PreviousError`
   assumption was FALSIFIED — not verifiable in modern VS); `],e`/`[,e`/`],w`/`[,w` → the
   BUILT severity-filtered navigator (pure `DiagnosticNavigator` + `ErrorListGatherer`,
   in-file, NO wrap, no-op logged). e2e gates ran GREEN (E2E-GAP3-1..2 discharged).
8. **Gap 4: Recent files finder** (LazyVim gap-analysis, triage=BUILD 2026-09-28) —
   Telescope-style recent-files finder. Feature lane. Plan in `docs/plans/backlog-plans.md`.
9. **Gap 5: LSP symbols finder** (LazyVim gap-analysis, triage=BUILD 2026-09-28) —
   Telescope-style document/workspace symbols finder. Feature lane. Plan in
   `docs/plans/backlog-plans.md`.
10. **Gap 6: Goto-definition finder + wire VsVim gd/gr/gi to the Telescope finders**
    (LazyVim gap-analysis, triage=BUILD 2026-09-28) — a goto-definition finder; VsVim's
    `gd`/`gr`/`gi` trigger the Telescope finders (references for `gr`, implementation for
    `gi`, goto-definition for `gd`). Feature lane. Plan in `docs/plans/backlog-plans.md`.
11. **Gap 8: Quickfix / code-actions picker** (LazyVim gap-analysis, triage=BUILD custom
    view 2026-10-03) — use the native Error List data but display it in a **custom
    Telescope-style view**; **folded into feature 8** (the `Leader+C+A` code-actions picker
    must show all possible code actions for the current selection/at-caret, with
    **quick-fix actions at the top** and the **warning-fix action first**). Feature lane.
    Plan in `docs/plans/backlog-plans.md`.
12. **Gap 9: Search/replace** (LazyVim gap-analysis, triage=BUILD custom overlay
    2026-10-03) — extend/reuse native replace semantics but build a **custom overlay**:
    two input fields (search, replace); seed the search from the current selection, or the
    word at the caret if there is no selection; navigate between hits; replace the current
    hit; replace all. Feature lane. Plan in `docs/plans/backlog-plans.md`.
13. **Gap 10: Hover/signature** (LazyVim gap-analysis, triage=BUILD custom overlay
    2026-10-03) — extend/reuse native QuickInfo/ParameterInfo data but build a **custom
    focusable overlay** navigable with vim motions (to read docs/help/notes). Feature lane.
    Plan in `docs/plans/backlog-plans.md`.
14. **Gap 11: Git** (LazyVim gap-analysis, triage=EXTEND/REUSE native 2026-10-03) — add
    leader bindings for the native git diff/blame/log/history commands (verify exact
    command names live). Small feature. Plan in `docs/plans/backlog-plans.md`.
15. **Which-key popup** (LazyVim gap-analysis, triage=BUILD 2026-10-03) — built **last**;
    deliver **BOTH** a bottom-right non-focus-stealing popup overlay (LazyVim-style) AND an
    inline popup variant, so both can be tested. Feature lane. Research in
    `docs/plans/whichkey-research.md`.

## User-requested features (added 2026-09-19, not yet started — pick after the in-flight explorer items)

> **FEATURE TRIAGE RULE (user instruction 2026-09-19 — applies to ALL feature
> backlog items):**
> 1. **LazyVim is the main reference** for how a feature *should work* — research
>    its workflow/functionality first and design against it.
> 2. **Before implementing ANY feature, ASK the user** (via the `question` tool)
>    whether it is worth the implementation time, OR whether there is a **better
>    native VS option to extend/reuse** (e.g. VS already updates references when
>    you move/rename a file in Solution Explorer — extend/reuse that QoL rather
>    than reimplementing it).
> 3. The **user decides** (build vs extend/reuse vs skip) BEFORE the loop starts a
>    feature item. Do not dive into a feature pipeline without this gate.
> This applies to items 6-9 below (and any future feature). The loop pauses at
> the triage gate for each new feature.

> **FOLLOW-UP (after the in-flight + queued work completes):** run a **LazyVim
> gap-analysis pass** over ALL the already-implemented features (leader-key
> bindings, window nav, Telescope finders, tool-window controllers, vim motions,
> etc.) to catch anything MISSED compared to LazyVim. Every gap found goes
> through the SAME feature-triage gate (ask the user: build vs extend/reuse
> native VS) before implementation. Schedule this AFTER:
> (a) the in-flight tree-select item is GREEN (`explorer-open-navigation`), and
> (b) `explorer-open-searchbox` is fixed. This is a review/triage pass, not an
> auto-implement task.

> These were requested by the user before sleep; they are NEW feature items.
> Implementation order is TBD (the user said "we will decide the order later"
> for the code-actions picker). Each needs its own feature-lane pipeline (M-M7
> applies to any that add a `[Telescope]`/`[NeoVisual]` diagnostic).

6. ~~**Vim motions in the Solution Explorer search box.**~~ **DONE — GREEN 2026-10-04.**
   The search box now routes the full normal-mode motion set h/l/w/b/e/a/A/I + j/k/0/$
   via `TextMotionHelper` while a WPF TextBox is focused (j/k are single-line no-ops,
   0/$ move to line start/end); the white block caret is applied in normal mode.
   `TextMotionDispatcher.MapKey` maps J/K/D0/D4; `SolutionExplorerController._actions`
   carries D0/D4; the `FocusGuard` shift overload exempts a focused search-box TextBox
   (`WindowManager.IsFocusedTextBoxInCurrentToolWindow`). New e2e scenario
   `explorer-searchbox-motions`; unit suite 168 → 171. Feature lane.

7. **Ctrl+H/J/K/L navigation INSIDE the Telescope overlay — 3 panes, modal.**
   The overlay should have **3 windows: input field (prompt), results list,
   preview pane**. Ctrl+H/J/K/L moves focus BETWEEN these 3 panes WITHOUT giving
   focus to any VS window below (modal — no `WindowMatrix`/window navigation; the
   overlay keeps focus). Currently Ctrl+H/Ctrl+L switch List↔Preview
   (`_focusTarget`); this extends to a 3-way pane switch including the prompt
   input, with Ctrl+J/K (up/down) also in play. Feature lane (new diagnostics
   likely, e.g. `[Telescope] focus target=Input|List|Preview`).

8. **`Leader+C+A` — code-actions picker.** Opens a picker of the VS **code
   actions** available at the caret, navigable with **Ctrl+N/P** (and **hjkl**).
   It must **differentiate whether it was triggered on a SELECTION (multi-line
   selected) vs just placed on a symbol** — the set of actions shown depends on
   that. It displays all available actions; **the ORDER is TBD, with one fixed
   rule: the FIRST option is always the action that fixes the warning/error**
   when the cursor was on a symbol marked as a warning/error (the fix-it action
   surfaces first).    Diagnostic contract + keybinding (`C,A` →
   `command:View.QuickActions` is CURRENTLY bound — will be REBOUND to this
   picker; the quick-actions menu itself is the native equivalent). Feature
   lane.

9. **Solution Explorer normal-mode `r`/`a`/`m` — refine to VS QoL semantics, or
   replace the native dialogs with a controlled vim-mode overlay.**
   **Base keys ALREADY EXIST** (`SolutionExplorerController`): `r` rename (F2
   injected), `a` Add Item command, `m` Move command; live scenarios
   `neovisual-explorer-rename`/`-add`/`-move` PASS. **The user's refinement:** the
   native VS dialogs (rename F2 edit box, Move dialog, Add Item dialog) are
   standard WinForms/WPF controls with **NO vim motions / insert-normal mode** —
   so if we cannot add vim-mode to them, **build a Telescope-like controlled
   overlay for rename/move/add** that WE own (with insert/normal mode + vim
   motions, like the other Telescope surfaces). Design grounded in **LazyVim**
   (research done 2026-09-19):
   - **LazyVim rename symbol** (`<leader>cr` = `vim.lsp.buf.rename`): inline LSP
     symbol rename, insert-mode, renames all references. (VS analog:
     `Refactor.Rename` / editor F2.)
   - **LazyVim rename/move FILE** (`<leader>cR` = `Snacks.rename.rename_file`):
     the **LSP file-rename flow** that fixes references: (1) send
`workspace/willRenameFiles` → (2) server returns a WorkspaceEdit (an LSP
    edit batch) of all import/reference updates → (3) apply+persist the edit →
    (4) rename on disk →
     (5) send `workspace/didRenameFiles`. **Known gotcha:** the edits may only be
     applied to *loaded buffers*, not persisted to disk before the filesystem
     rename — unloaded files must get their edits applied directly to disk
     (Snacks.rename / snacks-rename-fix handle this). This is the reference-fixing
     QoL the user wants.
   - **LazyVim code-action** (`<leader>ca` = `vim.lsp.buf.code_action`, mode
     `{"n","x"}`): normal + visual modes — the **selection-vs-symbol
     differentiation** maps exactly to the user's item #8 requirement.
   - **LazyVim add**: file created by entering its path directly (no native
     "Add New Item" dialog).
   - **Open feasibility question for the planner:** VS's Roslyn workspace
     already does file-rename/move-with-reference-fixing natively, but the public
     trigger is the native `SolutionExplorer.Move` dialog (which has the
     "update references" option). The item must determine HOW to trigger VS's
     reference-fixing rename/move **programmatically** (bypassing the native
     dialog) so it can be wrapped in a controlled vim-mode overlay — OR whether to
     use VS's Roslyn workspace file-operation APIs directly.
   Feature lane (new diagnostics for the controlled overlay + the reference-fixing
   proof — e.g. a post-move build/compile-check or reference-grep).

## Done (durable completion history — appended on every GREEN)

- **2026-10-04 — Columns UX bugfix (no h-scroll, all columns visible, min/max widths,
  logical shortening, column-relative window width, the selection contrast)** (Lane:
  `bugfix + UX`, e2e ENABLED; 3 delegations, 1 VS boot, 0 iterations). Fixes + refines the
  just-landed columns feature per the user's 2026-10-04 reports: the results list NEVER
  scrolls horizontally (`ScrollViewer.HorizontalScrollBarVisibility="Disabled"`, vertical
  Auto unchanged) and ALWAYS shows every column — each column has **min/max widths** (the
  pinned 11-row table across all 23 catalog sites: file 6/30 Tail · dir 6/40 Tail · path
  10/60 Tail · kind 3/8 End · message 10/∞ End · line 2/5 End · access 2/4 End ·
  symbol(Refs) 6/24 End · symbol(Impl) 6/∞ End · column 2/8 End · text 10/∞ End); the pure
  `ColumnWidths.Compute` distributes the surplus in priority order (never below min / above
  max; the exact-total invariant; the degenerate available<sum(MINs) case → the mins win and
  the caller widens the window); **logical shortening** — `ColumnTruncation.TailTruncate`
  removes the FRONT of path-like columns keeping the tail (`…\Services\Order.cs` — the end
  folder + file name survive; Telescope.nvim `path_display="truncate"` is the reference),
  `EndTruncate` removes the END of text columns; the **overlay width scales with the visible
  column count** (`min(max(760, NeededWidth+18+480+22), WorkArea.Width)`, recomputed at open
  + every chooser toggle; the results column = Pixel(Width−502), Compute's available = that
  −18 — more columns grow the window instead of eating the preview); the **selection
  contrast** (SelectionHighlightBrush #2d4a75 + SelectionForegroundBrush white, the
  IsSelected trigger sets both, active + inactive states; the per-cell Foreground removed).
  NO new diagnostic — the existing `[Telescope] results columns=` / `results count=` lines
  are byte-stable (proven at the source-diff AND live-log level). Unit tests: Telescope
  208 → **221** (13 new: 7 `Run_ColumnWidths_*`, 2 `Run_TailTruncate_*`, 2 `Run_EndTruncate_*`,
  `Run_ResultRowCells_Truncation_Kinds`, `Run_ResultsColumns_MinMaxWidths`); NeoVisual 190
  untouched. Final gates: `dotnet build` 0 errors; Telescope.Tests **221 passed, 0 failed**;
  NeoVisual.Tests **190 passed, 0 failed** (staggered); full 41-scenario e2e suite GREEN on
  a fresh boot (run 169, `-TimeoutSec 2400`: 41/41, zero flakes, `telescope-results-columns`
  first-try); both lints PASS; `-SelfCheck` PASS; `-List` 41; the 7 visual ACs verified by
  the MANUAL visual pass (code lines + the unit-pinned invariants). Deviations adjudicated
  (hub): BP-5 return type `IReadOnlyList<string>` (the artifact's `string[]` snippet
  contradicted the test contract) → ACCEPT; BP-10 placement (spec.md §2.5 + SKILL.md) →
  ACCEPT. Hub pre-VERIFY lint fix: `check-doc-refs.ps1` gained a doc-scoped allowlist entry
  for `.opencode/agent/trailmark-guidance.md` (GraphStore/CodeGraph — Trailmark Python
  types unresolvable in the C#-scoped lint).
  **Change summary:** created `Telescope/Overlay/Utils/ColumnWidths.cs` +
  `Telescope/Overlay/Utils/ColumnTruncation.cs` (both pure, dependency-free); modified
  `Telescope/Overlay/Utils/ResultColumn.cs` (+`ResultColumnTruncation` enum, +Min/Max/
  Truncation, ctor 6→9 params), `Telescope/Overlay/Utils/FinderColumns.cs` (the 23 sites
  pinned), `Telescope/Overlay/Utils/ResultRowCells.cs` (+the truncating Compute overload),
  `Telescope/Overlay/TelescopeOverlay.cs` (the h-scroll, the selection brushes + trigger,
  the Pixel results column, `ApplyWindowWidth`/`ApplyComputedColumnWidths`, the truncation
  in `RebuildRows`), `tests/Telescope.Tests/Program.cs` (+13 tests), `docs/spec.md` §2.5 +
  SKILL.md (the column-model passage), `tools/lint/check-doc-refs.ps1` (the allowlist
  entry). **If this regresses, look first at `ColumnWidths.Compute` (the distribution +
  the exact-total invariant — pinned by `Run_ColumnWidths_*`) and
  `TelescopeOverlay.ApplyComputedColumnWidths`/`ApplyWindowWidth` (the wiring: the Pixel
  results column, the recompute at open + chooser toggle) — the two places the unit tests
  and the e2e scenario assert on; the truncation contract is pinned by
  `Run_TailTruncate_*`/`Run_EndTruncate_*`.**

- **2026-10-04 — Gap 6 core: goto commands (`gd`/`gI`/`gr` → 1-hit direct / multi-hit
  Telescope)** (Lane: `feature`, e2e ENABLED — full pipeline: e2e RED before the build,
  executed at VERIFY; 5 delegations, 4 VS boots, 1 iteration). Three VS commands —
  `MyExtension.GotoDefinition` / `MyExtension.GotoReferences` / `MyExtension.GotoImplementation`
  (a NEW `MyExtensionPackage.vsct` — none existed before; + the VSCTCompile item +
  the ProvideMenuResource attribute; flags CommandWellOnly, no visibility
  constraints): each resolves the caret symbol (the shared `RoslynGatherers.TryGetCaretSymbol`
  seam) → gathers → the pure `GotoDispatcher.Decide(count)` seam
  (`Telescope/Controller/`, 1 → DirectJump; 0/>1 → OpenOverlay — LazyVim parity) →
  direct-jump via `HitOpener` (logging `[Telescope] goto-direct finder=… file=… line=…`) or
  `TelescopeLauncher.Open(finder)`. NEW `DefinitionFinder` (Telescope, `Name="Definition"`):
  the caret symbol's definition locations via Roslyn `DeclaringSyntaxReferences` +
  `Locations.Where(IsInSource)` fallback + (path,line) dedupe (gather-side) +
  FINDER-SIDE `OrderBy(FilePath).ThenBy(LineNumber)` (deterministic); display
  `{Kind} {SymbolName} — {file}:{line}`; registered in `MyExtensionPackage` +
  `TelescopeLauncher.FinderNames` (`telescope-definition` auto-derives the 17th registry
  entry). NEW M-M7 literal family (6, byte-exact): `definitions gathered count=` /
  `opened definition:` / `goto-direct finder=… file=… line=…` / `definitions gather failed:` /
  `open definition failed:` / `[NeoVisual] goto failed: {finder}: {msg}`. The USER maps
  `gd`/`gI`/`gr` to the commands in VsVim themselves (their explicit wiring decision — no
  hook/leader changes; the docs record the command names + a worked mapping example). New
  e2e scenario `telescope-goto` (41st; 4 parts via DTE command execution over the seeded
  GotoProbe partial-class pair) — created + PROVEN RED (DTE unknown-command rejection)
  before the build, PASSED its first live run. Unit tests: Telescope 199 → **208** (6
  `Run_DefinitionFinder_*` + 3 `Run_GotoDispatcher_*`); NeoVisual 190 (registry 16 → 17).
  **Verify-time iteration (1):** two harness-layer REDs fixed (a) `telescope-goto` Part 4's
  order-dependent caret state (the preceding scenario leaves the caret ON `IShape`; re-opening
  restores it → the walk lands past the symbol → gather 0) — fixed with a `gg` caret
  normalization before the walk; (b) `neovisual-window-management`'s 3rd-strike flake
  UPGRADED to a REGRESSION per the M-M2 budget and FIXED (step 0 opens Alpha.cs via the
  overlay + `Wait-ActiveDocumentMatch` editor-focus assert before the binding steps — the
  doc-window commands need editor focus). Harness finding: the DEFAULT 300s suite budget
  cannot fit 41 scenarios — the full-suite gate uses `-TimeoutSec 2400` (the default-budget
  raise is a filed hardening item). Final gates: `dotnet build` 0 errors; Telescope.Tests
  **208 passed, 0 failed**; NeoVisual.Tests **190 passed, 0 failed** (staggered); full
  41-scenario e2e suite GREEN on a fresh boot (run 168, `-TimeoutSec 2400`: 41/41, zero
  flakes); both lints PASS; `-SelfCheck` PASS; `-List` 41.
  **Change summary:** created `MyExtension/MyExtensionPackage.vsct`,
  `Telescope/Controller/GotoDispatcher.cs`, `Telescope/Finders/DefinitionFinder.cs`,
  `Telescope/Finders/Utils/DefinitionHit.cs`; modified
  `MyExtension/Package/Utils/TelescopeCommand.cs` (CommandList 0x0101-03),
  `MyExtension/MyExtension.csproj` (VSCTCompile), `MyExtension/Package/MyExtensionPackage.cs`
  (ProvideMenuResource + the finder registration + AddGotoCommand/ExecuteGoto),
  `MyExtension/Package/RoslynGatherers.cs` (GatherDefinitions/MapDefinitionHits),
  `MyExtension/Package/Utils/TelescopeLauncher.cs` (FinderNames), `tools/harness/test-e2e.ps1`
  (the GotoProbe seed pair + the scenario + the two debug fixes),
  `tests/Telescope.Tests/Program.cs`, `tests/NeoVisual.Tests/Program.cs` (registry 17), docs.
  **If this regresses, look first at `ExecuteGoto`'s dispatcher branch (a wrong Decide
  argument sends 1-hit gathers to the overlay) and `DefinitionFinder.GatherHits`'s finder-side
  ordering (the determinism test pins it) — the two places the e2e parts assert on; for the
  VsVim mapping, the docs' command names are the contract.**

- **2026-10-04 — Telescope results columns + preview-as-editor** (Lane: `feature`, e2e
  ENABLED — full pipeline: e2e RED before the build, executed at VERIFY; 7 delegations,
  5 VS boots, 1 iteration). The overlay's results list migrated from a read-only TextBox
  (`> `-marked rows) to a real **ListView+GridView** (headers visible, `Focusable=false`,
  selection programmatic): 23 catalog columns across the 6 finders (Files 3, Issues 4,
  References 6, Grep 3, Fzf 3, Implementation 4), the user's marks default-ON (Files:
  name+dir; Issues: kind+file+message; References: access+file; Grep/Fzf: file+line+text;
  Implementation: kind+file), **right-click a header → the full-catalog chooser menu**
  (toggle any column; catalog order stable; the LAST visible column cannot be hidden);
  abbreviated cell values (access W/R; issues todo/err/warn/info; implementation
  cls/inf/str/enm/func/prop/evt/imp… + the ≤4-char fallback). NEW diagnostic (M-M7):
  `[Telescope] results columns={ids}` (logged on render + on every chooser toggle); the
  `results count=N selected=M boxText=L` line kept BYTE-STABLE (the length = the rendered
  row text from the visible cells — zero harness edits at the 22 sites). The preview
  RichTextBox + custom SyntaxHighlighter/PreviewRenderer/PreviewTokenCache **RETIRED**
  (deleted, no fallback) → a REAL read-only VS editor view hosted via the new
  `IPreviewEditor` seam (`MyExtension/Package/Utils/PreviewEditorHost.cs`:
  `CreateAndLoadTextDocument` + content type by extension + roles
  Document+Interactive+Zoomable EXCLUDING `Editable` — VsVim never attaches; mtime
  create-or-reuse; dispose-on-close) + the pure `PreviewCaretMap`/`PreviewDiagnostics`
  seams (the navigator computes the target; the editor caret moves; `preview file=`/
  `preview caret=` byte-identical). fzf/ResultMapper untouched (Display stays the filter
  ordinal; payload by index). New e2e scenario `telescope-results-columns` (40th) — created
  + PROVEN RED (the missing `results columns=` contract) before the build, PASSED its first
  live run. Unit tests: Telescope 172 → **199** (−12 tokenizer − 4 legacy formatter
  + 25 columns + 5 KindAbbrev + 8 formatter seams + 5 preview); NeoVisual 190 untouched.
  **KNOWN LIMITATION (documented; the fix is OWNED BY THE PLANNING HUB — in flight):** the
  workspace-detached preview buffer gets NO Roslyn C# classifier → `preview tokens=0` (no
  semantic highlighting); the harness asserts the line's PRESENCE only (`tokens=\d+`) until
  the workspace-attach fix lands (spec §4 + AGENTS.md document it in place).
  Final gates: `dotnet build` 0 errors; Telescope.Tests **199 passed, 0 failed**;
  NeoVisual.Tests **190 passed, 0 failed** (staggered); full 40-scenario e2e suite GREEN on
  a fresh boot (run 162: 39/40 + 1 flaky — `neovisual-window-management` pass-on-retry,
  cumulative flaky count 2, hardening queued); both lints PASS; `-SelfCheck` PASS;
  `-List` 40.
  **Change summary:** created `Telescope/Overlay/Utils/ResultColumn.cs` (+ColumnVisibilityModel),
  `KindAbbreviations.cs`, `FinderColumns.cs`, `ResultRowCells.cs`, `IPreviewEditor.cs`,
  `PreviewCaretMap.cs`, `PreviewDiagnostics.cs`,
  `MyExtension/Package/Utils/PreviewEditorHost.cs`; modified
  `Telescope/Overlay/TelescopeOverlay.cs` (the ListView swap + chooser + preview hosting),
  `Telescope/Overlay/Utils/ResultsFormatter.cs` (ToText deleted → RenderedTextLength +
  ColumnsIdList), `Telescope/Controller/TelescopeController.cs` (the factory injection),
  `MyExtension/Package/MyExtensionPackage.cs`, `tools/harness/test-e2e.ps1` (the scenario +
  6 extensions + the tokens presence relaxation), `tests/Telescope.Tests/Program.cs`,
  `tools/lint/check-doc-content.ps1`   (the DOC-67-2 seam swap), `tools/lint/check-doc-refs.ps1`
  (the deleted-path allowlist for the historical archives), docs; DELETED the retired
  SyntaxHighlighter / PreviewRenderer / PreviewTokenCache sources (plain names — the files
  no longer exist). **If this regresses,
  look first at `TelescopeOverlay`'s render path (`RebuildColumns`/`RebuildRows`/
  `RenderResults` — the `results columns=` + byte-stable `count=` contract) and
  `PreviewEditorHost` (the role set must never include `Editable`; the mtime reuse key) —
  the two highest-risk changes.**

- **2026-10-04 — Gap 3: Diagnostics navigation (`]`/`[` prefix)** (Lane: `feature`, e2e
  ENABLED; 6 delegations, 4 VS boots, 1 iteration). LazyVim-style diagnostics navigation:
  `Space ],d`/`[,d` fire the native in-file squiggle commands
  (`command:Edit.GotoNextIssueinFile` / `command:Edit.GotoPreviousIssueinFile` — the
  backlog's `Edit.NextError`/`Edit.PreviousError` assumption was FALSIFIED, not verifiable
  in modern VS); `Space ],e`/`[,e`/`],w`/`[,w` fire the NEW built-in actions
  `next-error`/`prev-error`/`next-warning`/`prev-warning` — a severity-filtered navigator
  BUILT because native VS has no severity-specific commands: pure `DiagnosticNavigator`
  seam (+ `DiagnosticEntry` readonly struct; strict `>`/`<` comparisons — standing on a
  diagnostic and pressing `]e` moves to the NEXT one; NO wrap; no internal sort) +
  `ErrorListGatherer` (the CodeIssuesFinder-established DTE2 Error List API:
  `FileName`/`Line`/`ErrorLevel`/`vsBuildErrorLevelHigh|Medium`; file+severity filter,
  sort+dedup) + `InputHandler.NavigateDiagnostic(forward, severityError)` (exactly ONE
  outcome line per call; opens via the shared `DteFileOpener.OpenAtLine`). `KeyNames` gained
  `OemCloseBrackets`→`]` / `OemOpenBrackets`→`[`; six new leader bindings (30 → 36; no bare
  `]`/`[` — the prefix trap); Actions registry 12 → 16. NEW diagnostic family (M-M7):
  `[NeoVisual] diagnostic-nav direction=next|prev severity=error|warning target=<file>
  line=<n>` / `no-op: no-active-document|no-entries|at-end` / `failed: {msg}`. New e2e
  scenario `neovisual-diagnostic-nav` (39th) — PASSED its first live run. Unit tests
  177 → 190 (8 `Run_DiagnosticNavigator_*` + `Run_Keybinding_DefaultFileHasDiagnosticNav` +
  `Run_KeyNames_RoundTrip_DiagnosticNav` + registry 16 + `]`/`[` mappings).
  **Verify-time iteration (1):** the full-suite gate surfaced two REAL Gap 1 defects
  (both fail-twice) — fixed in plan Phase 7 (BP-H1..H3): (1) `LeaderSequenceMatcher` now
  treats modifier keys as TRANSPARENT while a sequence is active (down+up; the Shift chord
  for `|` was appended into the pending sequence → Abort → `w,|` could never fire — first
  firing in run 155) + 3 matcher unit tests; (2) `neovisual-editor-insert`'s save step →
  `Save-AllDocuments $vs.Id` (the injected Ctrl+S chord is consumed by the focused editor's
  key-processing chain — VsVim in front of the shell dispatch); (3)
  `neovisual-window-management` step 4 gains `Enter-NormalContext` (the StartPage's
  input-mode controller swallowed the leader). Final gates: `dotnet build` 0 errors;
  NeoVisual.Tests **190 passed, 0 failed**; Telescope.Tests **172 passed, 0 failed**
  (staggered); full 39-scenario e2e suite GREEN on a fresh boot (run 156: 38/39 —
  `neovisual-window-management` flaky-on-retry ×1, PASS in run 157; harness
  order-dependency, hardening queued — see the Pending queue's HARDENING block); lints
  PASS; `-SelfCheck` PASS; `-List` 39. The queued gates **E2E-GAP1-1..5 + E2E-GAP3-1..2
  are discharged** (removed from `docs/e2e-queue.md` per its run-GREEN rule).
  **Change summary:** created `MyExtension/Input/Utils/DiagnosticNavigator.cs`,
  `MyExtension/Package/Utils/ErrorListGatherer.cs`; modified
  `MyExtension/Input/Utils/KeyNames.cs`, `MyExtension/Input/Utils/LeaderSequenceMatcher.cs`
  (BP-H1), `MyExtension/Input/InputHandler.cs`, `MyExtension/Package/Utils/Actions.cs`,
  `MyExtension/Resources/default-keybindings.json`, `tools/harness/test-e2e.ps1` (BP-H3),
  `tests/NeoVisual.Tests/Program.cs`, docs. **If this regresses, look first at
  `ErrorListGatherer.Gather` (severity/file filter + sort/dedup — a wrong filter logs
  `no-op: no-entries` on a populated Error List) and `InputHandler.NavigateDiagnostic`'s
  outcome-line contract (exactly one line per call) — the two places the e2e assertions
  depend on; for leader-sequence regressions, look at BP-H1's modifier-transparency block
  (a wrong comparer/veto there aborts every chord-typed binding like `w,|`).**

- **2026-10-04 — Gap 1: Window-management leader bindings (`w` prefix) + case-sensitive
  leader combos** (Lane: `feature`, unit-only, e2e deferred; 3 delegations, 0 VS boots,
  0 iterations). `w` is now the window-management prefix: `w,-` →
  `command:Window.NewHorizontalTabGroup` (split below), `w,|` →
  `command:Window.NewVerticalTabGroup` (split right), `w,d` → the new focus-aware
  `close-window` built-in action (tool window → `Window.CloseToolWindow`, else
  `Window.CloseDocumentWindow`, via the pure `CloseWindowCommand` seam +
  `InputHandler.CloseWindow`). The `Space+W` save binding is REMOVED (the user saves with
  Ctrl+S) — a lone Space+W consumes and waits, firing nothing. Leader combos are now
  **CASE-SENSITIVE**: `KeyNames.ToString(Keys, bool shift)` encodes a letter's case
  (lowercase = unshifted, uppercase = Shift+letter; non-letters shift-insensitive),
  `LeaderSequenceMatcher` appends shift-aware names + an Ordinal prefix set,
  `InputHandler.BuildBindings`' leader dict and `KeybindingConfig.Merge`'s bindings map are
  Ordinal (the simple-shortcut path stays case-insensitive `Ctrl+H`), and all 24 existing
  leader keys migrated to lowercase in `default-keybindings.json` (28 → 30 bindings).
  `KeyNames.ToString` gained `OemPipe` → `|`. Actions registry 11 → 12 (`close-window`).
  New/updated unit tests (NeoVisual 171 → 177): 6 new
  (`Run_Keybinding_DefaultFileHasWindowManagement`, `Run_KeyNames_CaseEncodesShift`,
  `Run_KeyNames_RoundTrip_WindowPrefix`, `Run_LeaderSequenceMatcher_CaseSensitive`,
  `Run_LeaderSequenceMatcher_PrefixWaits`, `Run_CloseWindowCommand_For`),
  the former CaseInsensitive keybinding test renamed to `Run_Keybinding_CaseSensitive`,
  registry count 11 → 12,
  17 fixture migrations to lowercase Ordinal fixtures. Harness updated in place (NOT
  executed — e2e deferred): `neovisual-leader` reworked (Space w - + Space+e), NEW
  `neovisual-window-management` scenario registered (38th, queued E2E-GAP1-1),
  `neovisual-explorer-move-editor-focus` positive bound → Space+B,D (`b,d`),
  `neovisual-editor-insert` saves via Ctrl+S. Final gates: `dotnet build` 0 errors;
  NeoVisual.Tests **177 passed, 0 failed**; Telescope.Tests **172 passed, 0 failed**
  (staggered); `-SelfCheck` PASS; `-List` 38 scenarios; check-doc-refs 0 unresolved;
  check-doc-content PASS (12 assertions; the DOC-66-3 expectation was reconciled in
  `tools/lint/check-doc-content.ps1` — the lint was stale, not the Baseline). NO e2e run
  (user mandate); e2e gates QUEUED: E2E-GAP1-1..5 (`docs/e2e-queue.md`).
  **Change summary:** modified `MyExtension/Input/Utils/KeyNames.cs`,
  `MyExtension/Input/Utils/LeaderSequenceMatcher.cs`, `MyExtension/Input/InputHandler.cs`,
  `MyExtension/Input/Utils/KeybindingConfig.cs`,
  `MyExtension/Resources/default-keybindings.json`, `MyExtension/Package/Utils/Actions.cs`,
  `tools/harness/test-e2e.ps1`, `tools/lint/check-doc-content.ps1`,
  `tests/NeoVisual.Tests/Program.cs`, docs (spec.md / AGENTS.md / SKILL.md / progress.md /
  e2e-queue.md); created `MyExtension/Input/Utils/CloseWindowCommand.cs`. **If this
  regresses, look first at the case-sensitivity seam (`KeyNames.ToString(Keys, bool)` +
  `LeaderSequenceMatcher`'s Ordinal prefix set — a wrong comparer makes every leader
  binding abort) and the `w`-prefix group in `default-keybindings.json` (BP-B4's atomic
  rewrite) — the two highest-risk changes.**

- **2026-10-04 — Feature 6: Solution Explorer search-box vim motions** (Lane: `feature`;
  16 delegations, 2 VS boots, 0 iterations). Extended the Solution Explorer search box's
  vim motions to `j`/`k`/`0`/`$` (matching the text-input tool-window surfaces):
  `TextMotionDispatcher.MapKey` (WinForms) now maps `J`/`K`/`D0`/`D4`;
  `SolutionExplorerController._actions` gained `D0`/`D4` so the hook pre-filter routes
  them; the `FocusGuard` shift overload gained a scoped `textBoxFocused` exemption
  (`WindowManager.IsFocusedTextBoxInCurrentToolWindow`) so `$` (Shift+D4) reaches the box
  without regressing the `Shift+O/R/M/A/G` tree-action gate. Block caret already applied
  (confirmed). New e2e scenario `explorer-searchbox-motions` (37th). Final suites:
  NeoVisual.Tests 171, Telescope.Tests 172 (both 0 failed); full 37-scenario e2e suite
  GREEN; `dotnet build` 0 errors; check-doc-refs PASS.
  **Change summary:** modified `Telescope/Overlay/Utils/TextMotionDispatcher.cs`,
  `MyExtension/ToolWindows/SolutionExplorerController.cs`,
  `MyExtension/ToolWindows/Utils/FocusGuard.cs`, `MyExtension/ToolWindows/WindowManager.cs`,
  `MyExtension/Input/InputHandler.cs`, `tools/harness/test-e2e.ps1`,
  `tests/NeoVisual.Tests/Program.cs`, docs. **If this regresses, look first at
  `WindowManager.IsFocusedTextBoxInCurrentToolWindow` (the scoped shift exemption) and the
  `FocusGuard` shift overload — the two highest-risk changes.**
  Commit: `d5927e5`.

- **2026-10-03 — Telescope `fzf` finder** (Lane: `feature`; 18 delegations, 3 VS boots,
  1 iteration). New `FzfFinder` (Telescope, `Name="Fzf"`, `Space+F Z`) — a query-driven fuzzy
  **content** finder: per-query re-gather over `ProjectFiles.Enumerate` with fzf `--filter`
  fuzzy matching (one file at a time, mapped back via the pure `FzfLineMapper`), preview jumps
  to the hit line, Enter opens at the line. Falls back to a literal substring scan
  (`LiteralLineScanner`, shared with `GrepFinder`) when fzf is unavailable. New
  `IFinder.GetCandidatesAsync` seam (abstract on `IFinder`, implemented in `FinderBase<THit>`)
  so the overlay's query-driven path awaits the async fzf gather without blocking the UI
  thread. Confirmed the existing `FileFinder` (`Space+F T`) is the fuzzy file finder (no code
  change). New diagnostics: `[Telescope] fzf hits={count}`,
  `[Telescope] fzf unavailable — literal fallback`,
  `[Telescope] opened fzf: file={path} line={line}`. New e2e scenario `telescope-fzf` (36th).
  Final suites: **Telescope.Tests 172, NeoVisual.Tests 168** (both 0 failed); full
  36-scenario e2e suite GREEN; `dotnet build` 0 errors; check-doc-refs PASS.
  **Change summary:** created `Telescope/Filter/IFzfEngine.cs`,
  `Telescope/Finders/FzfFinder.cs`, `Telescope/Finders/Utils/FzfHit.cs`,
  `Telescope/Finders/Utils/FzfLineMapper.cs`, `Telescope/Finders/Utils/LiteralLineScanner.cs`;
  modified `Telescope/Filter/FzfFilter.cs` (`: IFzfEngine`),
  `Telescope/Finders/GrepFinder.cs` (`ScanFile` → `LiteralLineScanner`),
  `Telescope/Finders/TelescopeFinder.cs` (abstract `GetCandidatesAsync`),
  `Telescope/Finders/FinderBase.cs` (impl),
  `Telescope/Overlay/TelescopeOverlay.cs` (await),
  `MyExtension/Package/MyExtensionPackage.cs` (register),
  `MyExtension/Package/Utils/TelescopeLauncher.cs` (`FinderNames`),
  `MyExtension/Resources/default-keybindings.json` (`F,Z`),
  `tools/harness/test-e2e.ps1` (scenario + seed + anchor),
  `tests/Telescope.Tests/Program.cs` (+15), `tests/NeoVisual.Tests/Program.cs` (registry
  count 11), docs. **If this regresses, look first at `FzfFinder.GetCandidatesAsync` (the
  per-file fzf gather + `FzfLineMapper` mapping) and the
  `IFinder.GetCandidatesAsync`/`FinderBase` seam (the overlay await) — the two highest-risk
   changes.** Iteration-1 fix: `Run_ActionsRegistry_ContainsAllBuiltins` updated to 11 builtins
   (BP-13, test-only). Commit: `deebeb2`.

- **2026-10-02 — Code review fixes (72 findings)** (Lane: `bugfix`, **e2e ENABLED**;
  12 delegations, 2 VS boots, 1 iteration). All 72 findings (2 critical, 8 major, 44 minor,
  18 nit) from `docs/reviews/code-review.md` executed per-phase RED→GREEN (Phases 0-7).
  Final suites: **Telescope.Tests 157, NeoVisual.Tests 168** (both 0 failed); `dotnet build`
  0 errors; **full 35-scenario e2e suite GREEN** (fresh boot); all harness-health self-checks
  PASS (check-doc-refs 0 unresolved, check-doc-content 12/12, harness `-List` 35 scenarios,
  `-SelfCheck` 12/12, `dte-command -SelfTest` PASS).
  **Change summary:** Phase 0 (N1/N2 corrected tautological FocusGuard tests; N46/N47/N48
  dedupe; N49 delete; N50 restore static; N8 de-flake); Phase 1 (N4 block-caret deactivation
  removes the adornment; N3 `VimBufferSubscriptions` map-entry removal + Detach decrement;
  N28 VsVim resolution latch; N24 `EditorViewOpenedLog` dedupe); Phase 2 (N5 HRESULT check;
  N6 drop `NavigationSettings` cache; N7/N14 `MatchesPropertiesQuirk`; N11/N12/N15/N59/N60/
  N72 navigation robustness); Phase 3 (N16/N17/N18/N19/N20/N21/N22/N23/N25/N26/N51/N61/N62/
  N71 controllers + FocusGuard); Phase 4 (N29/N30/N56/N57/N58/N69/N70 input/hook); Phase 5
  (N27/N31/N32/N33/N34/N35/N36/N37/N38/N39/N40/N41/N42/N43/N44/N45/N63/N64/N65/N66/N67/N68
  Telescope overlay/finders/filter/logging); Phase 6 (N9/N10/N52/N53 harness + lint);
  Phase 7 (N54/N55 docs). New/changed diagnostics: `[Telescope] filter failed: {msg}` now
  emitted with exactly one prefix (N41/N63); `[NeoVisual] block-caret active=False` now
  truthful (N4). **If this regresses, look first at the two VERIFY-round-1 regression fixes:
  `TelescopeOverlay.EnterInsert` (now calls `_keyHandler.EnterInsertMode(placement)` before
  `FocusPrompt()`) and `WindowManager.ComputeTextInputSurfaceFocused` (COM DocView fallback
  for a focused `IWpfTextView` on a text-input tool window) — the two highest-risk changes.**
  Also fixed a pre-existing harness bootstrap bug in `tools/harness/dte-command.ps1`
  (the runspace arg binding used AddParameter instead of AddArgument) that blocked the e2e
  bootstrap (introduced `f4450cb`, never e2e-exercised since e2e was deferred).

- **2026-10-02 — Code review fixes (51 findings)** (Lane: `bugfix`, unit-only, e2e
  deferred; 11 delegations, 0 VS boots, 0 iterations). All 51 findings (0 critical, 5
  major, 22 minor, 24 nit) from `docs/reviews/code-review.md` executed per-phase
  RED→GREEN (Phases 0-9) in the unit-only lane. Final suites: **Telescope.Tests 153,
  NeoVisual.Tests 163** (both 0 failed); `dotnet build` 0 errors; all harness-health
  self-checks PASS (check-doc-refs 0 unresolved, harness `-List` 35 scenarios,
  `-SelfCheck` 12/12 incl. the R49 seed-leak standalone check). NO e2e harness commands
  run (machine cannot boot the VS Experimental Instance). e2e gates queued in
  `docs/e2e-queue.md` (E2E-CR51-1..10, status QUEUED).
  **Change summary:** Phase 0 (R1 prompt selection navigation — surface-aware
  `PromptMotionRouter` restriction + a/A/I placement routing; R2 preview document cache;
  R3 VimModeSource buffer-subscription lifecycle — Closed removal + refcount via the pure
  `VimBufferSubscriptions`; R4 per-frame fault isolation); Phase 1 (R5 telescope-mode
  i/a post-tap assertions; R13 shell-wait gated on harness env vars; R49 self-seeding
  subsets); Phase 2 (R6 `HasToolWindowActionKeys` deleted; R7 null-frame state reset;
  R10 shift gating for non-text-input controllers; R11 `IsKeyOfInterest` shift narrowed;
  R12 `InjectedKeyGuard` TTL-at-record); Phase 3 (R14 dead FocusedView/FocusedBuffer
  members deleted; R46 per-buffer-type delegate); Phase 4 (R15 Properties-window quirk
  single-source; R16 `GetWindowScreenRect` HRESULT + n19; R31 `_dte` readonly; R32
  linked-filter cache; R33 "Window navigator initialization failed" rename; R34
  `TryGetScreenRect` UI-thread assert); Phase 5 (R8 `FocusKeeper.Run` disposable; R9
  `PrimaryFilePath` empty-list guard; R17 focused-box reuse; R18 caret-relative slice;
  R19 `HierarchyWalker.FirstPathEndingWith` delegation; R20 instance-scoped
  `_defaultControllers` cache; R21 `BlockCaretAdornment` focus-loss handler; R35 H/L
  delegate to `TryMoveArrow`; R36 `MaxEscapeAttempts`; R37 redundant `_isInputMode`
  re-set dropped; R38 `editor-view-opened` exactly once; R39 text-motion sample
  sanitized); Phase 6 (R22 non-query fzf debounce; R23 fzf cancellation-task
  observation; R40 `ResultMapper` instance class; R41 single read in `PreviewRenderer`;
  R42 `IsAvailable` cached; R43 `LineIndex` binary search; R44 `SetText`-skip guard);
  Phase 7 (R28 `[MyExtension]` banner prefix; R29 `OnViewClosed` delegates; R30
  `NavigationSettings.Invalidate()`; R45 n16 disposition recorded — no code); Phase 8
  (R24 timing test deleted; R47 deterministic IsAvailableBounded test; R48 behavior-based
  GrepFinder test); Phase 9 (R25/R26/R27/R50/R51 docs drift). New/changed diagnostics:
  `[NeoVisual] Window navigator initialization failed` (R33, renamed from "Window
  matrix"), `[MyExtension] === Global Keyboard Logger Package STARTED ===` (R28,
  prefixed). **If this regresses, look first at the `PromptMotionRouter` surface-aware
  restriction + the `TryPromptMotion` placement routing (Telescope/Overlay/
  TelescopeOverlay.cs) and the `VimBufferSubscriptions` R3 lifecycle (MyExtension/Vim/
  Utils/VimBufferSubscriptions.cs) — the two highest-risk changes.**

- **2026-10-02 — Code review findings (98 findings) + Functional restructure** (Lane:
  `bugfix` + behavior-preserving restructure, unit-only, e2e deferred; 34 delegations, 0
  VS boots, 0 iterations). All 98 findings (0 critical, 8 major, 69 minor, 21 nit) from
  `docs/reviews/code-review.md` executed per-phase RED→GREEN (Phases 0-10) in the
  unit-only lane, plus the user-requested functional restructure (Phases 11-14: helpers
  into `Utils/` subfolders + 5 renames). Final suites: **Telescope.Tests 151,
  NeoVisual.Tests 158** (both 0 failed); `dotnet build` 0 errors; all harness-health
  self-checks PASS (check-doc-refs 0 unresolved, check-doc-content 12/12, harness
  `-List` 35 scenarios); log-literal diff gate PASS (only the approved additions m47×2 /
  n19 / m14×2 / n18 + the documented m6 single-stamp + n10 bare-contract changes). NO
  e2e harness commands run (machine cannot boot the VS Experimental Instance). e2e gates
  queued in `docs/e2e-queue.md` (E2E-NCR-1..2, E2E-NCR-M2/M4/M5/M3/M7/m47,
  E2E-RESTRUCTURE-2, E2E-NCR-BACKLOG, E2E-NCR-67-*, status QUEUED). Commit: `349fc05`.
  **Change summary:** Phases 0-10 (code-review fixes): M1/m52 `PromptMotionRouter`
  routing seam (a/A/I fall through to insert); M8 `HierarchyResolver.PrimaryFilePath`;
  M2/M4/m5/m27/m30-m34/m37/m50 finder-path amortization (GrepFinder drops Task.Run,
  `PreviewTokenCache`, FileContentCache LRU, ResultMapper byDisplay cache, FzfFilter
  availability cache); M5/m7/m42/m43/m47/n14-n16 FocusGuard + routing (3-arg
  `ShouldRouteToolWindowKey`, `InjectedKeyGuard` TTL, `navigate activated/no-op`
  diagnostics); m38-m41/m3/n1 Vim interop (mode-change on focus loss); m11-m14/m44-m49/
  n3-n6/n19 navigation robustness (symmetric tolerance, `window rect unavailable`
  diagnostic); m20-m26/m59/n7-n10 controller/state (per-type defaults, `RunGuarded`,
  .cs filter, stop-on-close); m8/m9/m16-m19/m35/n11 duplication merges (single
  `KeyToArrowVk`, W/B/E to base, `BlockCaretStyle` shared `ApplyCaretStyle`, `TryDispatch`
  merged into `TextMotionDispatcher`); m1/m2/m10/m28/m6/m29/m51/n2/n12/n13/n17/n18 dead
  code + logging (`[Hook]` single-stamp, `PaneFailureTracker` retry latch, `SetHook`
  MainModule guard); M3/m60/m63/m64/m65 harness hardening (`Send-Text` case-fidelity,
  per-line `Wait-LogLine`, `count=[1-9]\d*`); M7/m4/m53-m58/m62 test hermeticity
  (`FzfFilter.AwaitedReadCount`, `RoslynGatherers` extraction); M6/m61/m66-m69/n20/n21
  docs drift (E2E-NCR-67-* reconciliation, scoped doc-ref allowlist, spec.md §4/§2.2).
  Phases 11-14 (restructure): helpers into `Utils/` subfolders (MyExtension + Telescope);
  5 renames (WindowMatrix→WindowNavigator, CardinalNavigationConstants→NavigationConstants,
  UtilityMethods→WindowFrameUtils, RectCoordinate→WindowRect, WindowAdapter→WindowFrameAdapter);
  reference sweep. New diagnostics: `[NeoVisual] navigate activated index=...` /
  `navigate no-op: <reason>` (m47), `[NeoVisual] window rect unavailable; using empty
  rect` (n19), `[NeoVisual] IVsUIShell unavailable: ...` ×2 (m14), `[Hook] SetHook
  MainModule failed: {ex.Message}` (n18). **If this regresses, look first at the
  `PromptMotionRouter` routing seam (Telescope/Overlay/Utils/PromptMotionRouter.cs) and
  the `RoslynGatherers` extraction (MyExtension/Package/RoslynGatherers.cs) — the two
  highest-risk changes.**

- **2026-09-30 — Code review findings (67 findings) + Repository restructure** (Lane:
  `feature/bugfix program`, unit-only, e2e deferred; 25 delegations, 0 VS boots, 0
  iterations). All 67 findings (2 critical, 31 major, 32 minor, 2 nit) from
  `docs/reviews/code-review.md` executed per-phase RED→GREEN (Phases 0-10) in the
  unit-only lane, plus the user-requested repo restructure (Phases 11-14: folders +
  namespaces + tools + docs). Final suites: **Telescope.Tests 143, NeoVisual.Tests 140**
  (both 0 failed); `dotnet build` 0 errors; all harness-health self-checks PASS
  (check-doc-refs 0 unresolved, dte-command -SelfTest, iterate-telescope -SelfCheck,
  check-doc-refs -SelfCheck); log-literal diff gate PASS (no `[Telescope]`/`[NeoVisual]`/
  `[Hook]`/`[MyExtension]` literal changed by the restructure). NO e2e harness commands
  run (user mandate — VS-capable machine required). e2e gates queued in
  `docs/e2e-queue.md` (E2E-NCR-1..2, E2E-NCR-M1/M2/M15 .. E2E-NCR-M26/M27/M28,
  E2E-RESTRUCTURE-1, status QUEUED). Commit: `92b119f`.
  **Change summary:** Phases 0-5 (Chunk A): CR1/CR2 (TextInput action table + VimBufferSubscriptions),
  M1/M2/M3/M4/M5/M6/M7/M8/M9/M10/M11/M12/M13/M14/M15/M16/M19/M25 + m14/m15/m16/m17 (fzf
  off-thread spawn + timeout observation, FileContentCache LRU, ResultMapper unknown-display
  log, Preview blank-line Up fix, TextMotionDispatcher unification, VimModeTracker log-on-change,
  FilterFailureLog unprefixed format, toolwindow-move-failed diagnostic ADD, DTE? null-safety).
  Phases 6-10 (Chunk B): M18/M20/M21/M22/M24/M17/M23/M26/M27/M28 + m1-m32/n1/n2 (duplication
  merges, dead-code deletion, harness PID-scope + Process-scope env + regex tightening,
  test hermeticity + naming sweep, docs drift). Phases 11-14 (Chunk C): restructure to
  MyExtension/{Hooks,Input,Vim,Package,Adornments,Navigation,ToolWindows}/ +
  Telescope/{Overlay,Finders,Filter,Logging,Controller}/ + tools/{harness,lint}/ +
  docs/{reviews,plans}/ (namespaces renamed to match; `CardinalMovment/` → `Navigation/`
  with `MyExtension.Navigation`). New diagnostics: `[NeoVisual] toolwindow-move failed: {msg}`,
  `[Telescope] result-mapper unknown display: {display}`. **If this regresses, look first at
  the restructure's namespace mapping (Phase 11/12) and the M3 fzf timeout path
  (Telescope/Filter/FzfFilter.cs) — the two highest-risk changes.**

- **2026-09-30 — Code review findings (75 findings, 12 phases)** (Lane: `feature/bugfix
  program`, unit-only, e2e deferred; ~40 delegations, 0 VS boots, 0 iterations). All 75
  findings (3 critical, 30 major, 30 minor, 12 nit) from `docs/reviews/code-review.md` executed
  per-phase RED→GREEN in the unit-only lane. Phases 0-6 behavior fixes, 7-9 refactors,
  10-12 harness/test/naming. Final suites: **Telescope.Tests 135, NeoVisual.Tests 130**
  (both 0 failed); `dotnet build` 0 errors; all harness-health self-checks PASS
  (check-doc-refs 0 unresolved, dte-command -SelfTest, iterate-telescope -SelfCheck,
  check-doc-refs -SelfCheck). NO e2e harness commands run (user mandate — VS-capable
  machine required; the correct machine runs VS 2026). e2e gates queued in
  `docs/e2e-queue.md` (E2E-CR-1..3, E2E-M1..M42). Commit: `f4450cb`.
  **Change summary:** created ~20 new files (OverlayShowState, StaleToolWindowSentinel,
  KeyNameBuilder, KeyNames, NavigationSnapshot, HierarchyWalker, HitOpener, TryDispatch,
  BlockCaretStyle, FocusKeeper, VimModeClassifier, VimModeSource, InitSteps,
  SimpleShortcutMatcher, FocusTargetModel, FileContentCache, LineIndex, FilterFailureLog,
  PaneFailureTracker); deleted BuildSimpleKey/KeyToString/Classify(string)/BuildForest/
  OpenReference/OpenImplementation/FindFirstSourceFileInItems/CollectProjectFiles/
  CollectItems/ToDirection/LEFT-RIGHT-UP-DOWN constants/_rect/_keyboardLogger/expHive/
  Find-VsWindow; rewired InputHandler/WindowMatrix/WindowManager/MyExtensionPackage/
  VimModeTracker/TelescopeOverlay/SolutionExplorerController/PreviewRenderer/GrepFinder/
  CodeIssuesFinder/FinderBase/LogFileWriter/NeoVisualLog/Actions; consolidated the harness
  (Open-TelescopeFinder, Ensure-SolutionExplorerOpen, Wait-LogLine, Assert-Budget,
  Get-LogCacheIndex, -SelfCheck/-SelfTest seams, PID-scoped kills, Process-scope env,
  always-reset seeding). New diagnostics: `[NeoVisual] stale-toolwindow sentinel active`,
  `[NeoVisual] leader-binding failed:`, `[NeoVisual] shortcut-binding failed:`,
  `[NeoVisual] output pane unavailable:`, `[MyExtension] init <step> ok/failed:`,
  `[Telescope] fzf filter failed:`, `[Telescope] fzf unavailable — showing unfiltered
  list`, `[Telescope] filter failed:`. **If this regresses, look first at the extracted
  pure seams (OverlayShowState, VimModeState/VimModeClassifier, FocusGuard,
  SimpleShortcutMatcher, TryDispatch, HierarchyWalker, HitOpener) — each is unit-tested;
  the e2e gates are queued in `e2e-queue.md`.**

- **2026-09-28 — F15 CodeIssuesFinder per-open DTE re-enumeration** (Lane: `bugfix`;
  3 delegations, 0 VS boots, 0 iterations). `CodeIssuesFinder.GatherHits` re-enumerated
  the whole solution's project files on EVERY open. Extracted a pure `ProjectFileCache`
  (Get + Invalidate) and wired it into `GatherHits` with solution-change detection
  (invalidate when `dte.Solution.FullName` changes). Telescope 81→84, NeoVisual 81,
  all passing. (The "lazy/async TODO scan" half of the finding is deferred.)
  **Change summary:** created `Telescope/Finders/ProjectFileCache.cs` (pure cache); rewired
  `CodeIssuesFinder.GatherHits` (solution-change detection + `_fileCache.Get`); added 3
  `Run_ProjectFileCache_*` tests. **If this regresses, look first at
  `ProjectFileCache.Get` (the `_dirty` flag) and the `GatherHits` solution-change check.**
- **2026-09-28 — F12 Display-keyed payload lookup loses same-named duplicates** (Lane:
  `bugfix`; 3 delegations, 0 VS boots, 0 iterations). The overlay mapped fzf-matched
  display lines back to payloads by display text (`GroupBy(...).ToDictionary(g => g.Key,
  g => g.First())`), so two same-named entries (e.g. two `Program.cs`) collapsed to the
  first — the second became a null-payload entry that silently did nothing. Extracted a
  pure `ResultMapper.MapBack` (stable-ordinal consumption, preserves duplicates).
  Telescope 77→81, NeoVisual 81, all passing.
  **Change summary:** created `Telescope/Overlay/ResultMapper.cs` (pure `MapBack`); rewired
  `TelescopeOverlay.FilterAndUpdateAsync` to use it; added 4 `Run_ResultMapper_*` tests.
  **If this regresses, look first at `ResultMapper.MapBack` (the consumed-index
  duplicate handling) and the `FilterAndUpdateAsync` call site.**
- **2026-09-28 — F22 Leader state machine not extracted** (Lane: `bugfix`, lighter —
  behavior-preserving refactor; 3 delegations, 0 VS boots, 0 iterations). Extracted the
  leader-key sequence routing from `InputHandler` into a pure, dependency-free
  `LeaderSequenceMatcher` (the `OverlayKeyHandler` pattern): leader-key start, sequence
  building, binding match, prefix detection, abort. `InputHandler` delegates; the
  `leader-binding executed: {sequence}` diagnostic is byte-identical. NeoVisual 74→81,
  Telescope 77, all passing.
  **Change summary:** created `MyExtension/Input/LeaderSequenceMatcher.cs` (pure state machine +
  `LeaderResult`/`LeaderResultKind`); rewired `InputHandler` (leader block → matcher
  delegation, `IsLeaderActive`/`ResetSequence` delegate); added 7 `Run_LeaderMatcher_*`
  tests. **If this regresses, look first at `LeaderSequenceMatcher.HandleKey` (the
  Consume/Execute/Abort transitions) and the `leader-binding executed:` log site.**
- **2026-09-28 — Architecture consolidation (5 lanes)** (Lane: `refactor`, unit-only,
  e2e deferred; ~20 delegations, 0 VS boots, 0 iterations). ~30 under-factored seams
  collapsed into target APIs across 5 lanes: L1 mechanical dedup (NativeMethods,
  TelescopeLauncher, Actions, VsServices, DteFileOpener, TelescopeLog, CaretPlacement,
  IFinder query fold, shared TestRunner), L2 vim-motion/caret (TextMotionHelper engine,
  ActionTable, ToolWindowControllerBase, FocusGuard veto, ResolveTreeItem), L3 finder
  (FileLocation/IFileLocation/FileHit, FinderBase\<THit\>, PreviewRenderer,
  RoslynCaretContext, ControllerRegistry), L4 navigation (RectCoordinate readonly
  struct, NavigationSettings, WindowNavigationEngine, WindowAdapter, matrix cache),
  L5 cross-cutting (buffered LogFileWriter + NeoVisualLog.Debug/Close,
  harness-common.ps1, IsKeyOfInterest, ProjectLayering decision). Unit suites:
  Telescope 56→77, NeoVisual 38→74, all passing. NO `[Telescope]`/`[NeoVisual]`/`[Hook]`
  structured-log literal changed (one adjudicated deviation: hook prefix
  `[GlobalKeyboard]`→`[Hook]`).
  **Change summary:** created ~15 new files (NativeMethods, TelescopeLauncher, Actions,
  VsServices, DteFileOpener, TelescopeLog, FileLocation/IFileLocation/FileHit,
  FinderBase, PreviewRenderer, WindowAdapter, WindowNavigationEngine, NavigationSettings,
  Direction, ToolWindowControllerBase, harness-common.ps1, e2e-queue.md); deleted
  IVsFrameView/WindowControlAdapter/IVsUIWindowFrameExtractor/IQueryFinder + dead
  constants/filters; rewired InputHandler/WindowMatrix/MyExtensionPackage/WindowManager.
  **If this regresses, look first at the extracted pure seams (WindowNavigationEngine,
  FinderBase, TextMotionHelper, FocusGuard) — each is unit-tested; the e2e gates are
  queued in `e2e-queue.md` (E2E-AC-1..5).**
- **2026-09-28 — Unit tests triggered real keybd_event injection** (Lane: `bugfix`;
  3 delegations, 0 VS boots, 0 iterations). The unit tests called `TryMove` on the
  tool-window controllers, which called `KeyInjection.Press` → real `keybd_event`,
  injecting arrow/Enter/F2 keys into whatever window had OS focus (the user's active
  window). **Change summary:** added `KeyInjection.SimulateOnly` (Press records the VK
  in `InjectedKeyGuard` but skips `keybd_event` when set) + set it `true` in
  `tests/NeoVisual.Tests/Program.cs` `Main`; added `Run_KeyInjection_SimulateOnly`.
  **If this regresses, look first at `KeyInjection.Press` (the `if (SimulateOnly)
  return;` guard) and the `Main` flag.**
- **2026-09-27 — `neovisual-editor-insert` marker corruption** (Lane: `bugfix`,
  harness-only; 2 delegations, 5 VS boots, 1 iteration). The scenario was 2/2 RED and
  had been carried on the known-RED allowlist as an "IntelliSense flake" — it was
  neither intermittent nor a product bug. Root cause (**test artifact**): `i` was
  pressed with the caret at **document position 0** (code context), so C# IntelliSense
  auto-popped and the injected `Space` committed HandleInheritability, corrupting the
  marker; the hook had correctly routed every key.
  **Change summary:** `tools/harness/test-e2e.ps1` only — two `w` normal-mode motions move the
  caret into the `// Beta.cs` comment before `i`, so the marker lands where completion
  cannot fire. Marker string + keystrokes unchanged (insertion point only) → the test
  still fails closed on a swallowed key. **If this regresses, look first at the two
  `Send-Tap 0x57` motions before the `i` tap.** Full suite now **35/35** (the allowlist
  entry is closed). Commit: `fd18315`
- **2026-09-27 — `telescope-implementation` intermittent injected-Enter loss** (Lane:
  `bugfix`, harness-only; 3 delegations, 5 VS boots, 1 iteration). Root cause was a
  **harness focus race**, not a product bug: `Assert-OverlayFocused` only checked the
  foreground window's VS **PID**, which the VS *main* window also satisfies, so Enter was
  injected before the overlay became the OS foreground window (`Focus prompt => True` is
  WPF *logical* focus; probe showed the main window foreground before Enter, the overlay
  ~800ms after). Other finders masked it by typing a query first (~360ms).
  **Change summary:** `tools/harness/test-e2e.ps1` only — `Assert-OverlayFocused` now requires the
  actual overlay (PID **and** window text `Telescope`), via new `Get-ForegroundTitle` +
  positive `Wait-OverlayForeground`, wired into all five `Open-Telescope*` helpers + before
  the Step-5 Enter. **If this regresses, look first at `Assert-OverlayFocused` /
  `Wait-OverlayForeground`** (a false-strict gate would fail every telescope-* scenario).
  Also corrected the plan's red herring (`[Hook] key=Return` can never exist — `Keys.Return`
  is not in `IsInteresting`; the real line is `[Telescope] key=Return … handled=True`).
  Verified: `telescope-implementation` **3/3 sequential GREEN** (was 3/3 RED), 7/7 finder
  neighbours GREEN, full suite 34/35 (only the allowlisted `neovisual-editor-insert` flake),
  NeoVisual 38/38, Telescope 56/56. No product code changed. Commit: `7c6569b`
- **2026-09-27 — Tool-window action-key leak guard (`FocusGuard`)** (Lane:
  `bugfix (no-seam)`; 9 delegations, 6 VS boots, 1 iteration). **Closes the
  user-reported leak** where, with Solution Explorer unfocused, `m`/`o`/`r`/`a` were
  consumed as tree actions and typed into the editor (the `ljoljoljoljoljo` storm).
  Root cause: `WindowManager.IsToolWindow` is driven by VS's `SEID_WindowFrame`
  selection event and goes **stale** when focus moves to an editor.
  **Change summary:** new `MyExtension/ToolWindows/FocusGuard.cs` (pure:
  `ShouldRouteToolWindowKey`/`IsTyping`); `VimModeTracker`
  gained event-driven `IsEditorFocused`; `InputHandler` gates
  `ShouldRouteToolWindowKey`/routing/`ExitToolWindowInputMode`/`IsTyping` on
  `EditorFocusedVeto` (= `IsEditorFocused && controller not input-mode &&
  !IsTextInputType`, so the Command Window / an input-mode controller is never
  vetoed — DEVIATION **D4**, which fixed a fail-twice `neovisual-textinput-motions`
  regression); `WindowManager` gained a test-only `stale-toolwindow` sentinel.
  New **35th** scenario `neovisual-explorer-move-editor-focus` (deterministic,
  no timeouts/probes: sentinel + bounded absence scan). D-A/D-B/D-C ACCEPT (open/
  open-o gate strictly stronger; Gamma.cs; read-only GetActiveDocument).
  **If this regresses, look first at `EditorFocusedVeto` (`InputHandler.cs:82`) and
  the sentinel scenario's bounded absence scan.** Doc sync: 34→35 scenarios,
  NeoVisual 31→38 in spec/AGENTS/SKILL. Commit: `1f32d00` (its message says "36th" —
  a miscount; the actual count is 35).
- **2026-09-27 — `explorer-open-searchbox` search-box focus-exit gap** (Lane:
  `bugfix (no-seam)`, 5 VERIFY boots + 1 RED boot; attempt 1 GREEN after 1 regression
  iteration). **The last known-RED scenario is now GREEN — the 34-scenario suite has no
  known-RED left** (remaining failures are flaky/known-flaky + the separately-queued
  intermittent `telescope-implementation`). Fix: `ExitInputMode` captures the search-box
  query, and `ReturnFocusToTree(query)` resolves the query-matched tree node via the new
  pure `HierarchyResolver.FirstPathMatching` (VS's native search filter does NOT select
  the matching node — falsified live), injects a bounded (≤4) `VK_ESCAPE` loop while the
  box is still focused (Escape #1 only CLEARS the query — also falsified live), then
  `Select`s the target + a ~1.5 s keeper. **No new/changed `[Telescope]`/`[NeoVisual]`
  log literal** (M-M7 not triggered).
  **Change summary:** `MyExtension/ToolWindows/HierarchyResolver.cs` (+`FirstPathMatching`),
  `MyExtension/ToolWindows/SolutionExplorerController.cs` (`ExitInputMode`,
  `ReturnFocusToTree`, `SelectFirstSourceFile` now emits `editor-view-opened file=` for the
  file it opens and activates an already-open document), `MyExtension/Hooks/KeyInjection.cs`
  (`VK_ESCAPE`), `tests/NeoVisual.Tests/Program.cs` (+5, 26→31), `tools/harness/test-e2e.ps1`
  (seed-leak expected-result rework, teardown save, nav assertion re-scope),
  `tools/lint/check-doc-refs.ps1` (allowlist). **If this regresses, look first at
  `ReturnFocusToTree`'s bounded-Escape loop + `FirstPathMatching`, then
  `SelectFirstSourceFile`'s direct `editor-view-opened` emission (the D1 fix).**
  Doc sync: spec.md seed-leak section, AGENTS.md/SKILL.md counts (Telescope 56,
  NeoVisual 31). Commit: `7390e9d`
  - **D1 (ACCEPT):** `g` emits the existing `editor-view-opened file=` literal directly
    (activating an already-open view raises no `TextViewCreated` — the plan's premise was
    false); this fixed the full-suite `explorer-open-navigation` fail-twice.
  - **D2 (ACCEPT):** `explorer-open-navigation`'s `editor-view-opened` assertion was
    mislabelled "`o` opened the selected source file" but was in fact satisfied by the
    `g`-path emission — re-attributed to `g`; `o` asserts `solution-explorer open`. Not a
    weakening (the searchbox scenario still asserts the full open chain).
  - **D3 (ACCEPT, user-requested):** W22's snapshot+`$AllowLeak` guard was defective
    (counted `obj/` build artifacts + `neovascular-editor-insert`'s intentional `Beta.cs`
    save; the allowlist itself re-reported removed entries as "added"). Replaced by an
    **expected-result tree** (`log/seed-expected/`, byte-compared, no ignorelist) with the
    intentional write refreshing its expected copy in a `finally`; teardown now runs
    `File.SaveAll` before killing VS (leak evidence survives; no next-run
    "did not close properly" prompt).
  - **New queue items** (found while fixing): `neovisual-explorer-move` leaks `m` into the
    editor when SE is unfocused (#1) and `telescope-implementation` intermittent Enter
    (#5.5). Failure-log sweep: 2 entries read, 2 annotated, 0 queued-by-sweep, 0 new fixed.
  - `delegations: 4 | VS boots: 6 | iterations: 1`.
- **2026-09-27 — W24 (user-requested): make the failure log a per-run step** (Lane:
  trivial config edit; agent docs only — no source/tests/tools). Closes the gap that
  `.opencode/AGENT-FAILURES.md` (W23) had no consumer: a failure log nobody reads is
  just a diary. **`neovim_hub.md`** now has (a) a **Failure-log triage** bullet inside
  the existing harness-health gate (runs at every VERIFY, so it rides the existing
  checkpoint), and (b) a new **LOOP step 11 POST-RUN FAILURE-LOG SWEEP** mandatory at
  every item's final gate, before the GREEN commit — every `agent-syntax` entry closed
  by fixing a prompt/skill/agent file, every `tool-bug` filed as a queue item, every
  `environment` annotated with its workaround; the sweep result is recorded in the
  Execution Log as `failure-log sweep: N entries read, M fixed, K queued, J annotated`.
  **`.opencode/AGENT-FAILURES.md`** gained a "Rules for the hub (the sweep)" block
  (append-only, annotate-in-place, prune-by-fix-not-deletion) and its first entry was
  annotated `FIXED 2026-09-27` (the W23 doc fix). **Restart required** (agent files load
  at opencode startup). Commit: (recorded below)
- **2026-09-27 — W23 (user-requested): fix the recurring Trailmark `to_json()` shape
  errors + add an agent failure-log** (Lane: trivial config edit; docs/agent files only —
  no source/tests/tools). Root cause of the recurring
  `TypeError: string indices must be integers, not 'str'` in arch-auditors: (1)
  `QueryEngine.to_json()` returns a JSON **str**, so `to_json()['nodes']` indexes a string;
  (2) after parsing, `nodes` is an **id-keyed dict** (`{node_id: node_dict}`), not a list
  (`['nodes'][0]` → KeyError); `edges` IS a list. The correct idiom is
  `j = json.loads(e.to_json())` and — for caller questions — address the
  `proxy.unresolved:<Type>.<Member>` id DIRECTLY with `callers_of` (no node enumeration).
  **Files changed:** `.opencode/skills/trailmark/SKILL.md` (+ "Graph export shapes" note),
  `.opencode/skills/trailmark/references/query-patterns.md` (+ section + §9 export fix),
  `.opencode/skills/trailmark/references/preanalysis-passes.md` (one-liner),
  `.opencode/skills/trailmark-structural/SKILL.md` (one-liner),
  `.opencode/agent/arch-auditor.md` (+ verbatim snippet + rule),
  `.opencode/agent/trailmark-recon.md` (+ snippet + rule near the proxy trap),
  and NEW `.opencode/AGENT-FAILURES.md` (append-only log so agents record unintended
  command failures + triage category `agent-syntax` / `tool-bug` / `environment`).
  **Verified 2026-09-27** (no TypeError): `nodes` dict=True (557), `edges` list=True,
  `callers_of('proxy.unresolved:controller.TryMove')` → `['HandleKey']`,
  `callers_of('proxy.unresolved:controller.ExitInputMode')` → `['ExitToolWindowInputMode']`.
   Deliberately NOT changed (verified correct): `build_slice_packet.py`,
  `genotoxic/references/graph-analysis.md`. **Restart required** (agent/skill files load at
  opencode startup). **Follow-up (2026-09-27):** W23 introduced a blocking
  `check-doc-refs.ps1` failure — the backticked ``KeyError`` in `trailmark-recon.md` was
  unresolvable; added `TypeError`/`KeyError` to the lint's external allowlist (Python
  builtin exceptions, not C# symbols). Lint now
  `[PASS] 17 docs, 4252 refs, 0 unresolved`. Commit: (recorded below)

- **2026-09-27 — W22 (user-requested): `seed-leak` end-of-run leak guard** (Lane:
  bugfix/harness-only). `tools/harness/test-e2e.ps1` now takes a SHA-256 snapshot of every seeded
  file at bootstrap (right after the fresh reseed) and runs a
  new LAST scenario `seed-leak` that re-hashes the seeds at the END of the run and FAILS
  on any seeded file that was **added / removed / modified** during the run. Purpose: prove
  no e2e test wrote into a seeded file, so a real leak (a test that mutated a seed, or a
  future in-flight item that intends to) is caught rather than silently corrupting later
  runs. **SUPERSEDED DESIGN (2026-09-27, see the explorer-open-searchbox Done entry):** the
  original bootstrap wrote a SHA-256 snapshot (log/seed-baseline.json) and the scenario
  held an `$AllowLeak` filename list. **That design was broken and is replaced by an
  expected-result tree** (`log/seed-expected/`, byte-compared at the end — no ignorelist):
  (a) the snapshot counted `Probe/obj/**/*.cs` build artifacts as "added" seeds and
  `neovisual-editor-insert`'s intentional `Beta.cs` save as "modified" (a real full-run
  failure that W22's self-check-only verification missed); (b) the `$AllowLeak` mechanism
  itself was defective (deleting allowlisted entries from the snapshot re-reported them as
  "added"). The guard skips gracefully in `-NoBootstrap` reuse mode. Filesystem only — no
  keystrokes, no diagnostics (M-M7 N/A). Helpers `Get-SeedFiles` / `Write-SeedExpected` /
  `Update-SeedExpected` / `Assert-NoSeedLeak`; re-verified by a no-VS self-check (clean→pass,
  obj-artifact→pass, intentional Beta.cs→pass, foreign edit→fail, remove→fail). Scenario
  count 33→34 (32→33 passing) synced across spec.md / AGENTS.md / SKILL.md;
  `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W21: add docs/reviews/architecture-review.md to the doc-ref lint** (Lane: trivial
  config edit). `tools/lint/check-doc-refs.ps1`: added `docs/reviews/architecture-review.md` to the
  default `$Docs` set + the header list; taught `Test-PathRef` to strip `:N-M` line-range
  suffixes; added the "code smells / recently removed (F14) / proposed (F18) / forbidden
  (F46)" symbols to the external allowlist. `neovim_hub.md` step 9's doc-sync set now
  includes the report. Lint: `[PASS] 17 docs scanned, 4110 refs, 0 unresolved`.
  Commit: (recorded below)
- **2026-09-27 — W20: update the stale unit counts** (Lane: trivial config edit).
  `code-testing-agent/SKILL.md` now says 56/26 (+ a drift caveat); the stale 42/21 in
  progress.md was annotated "at the time" (W12). **Restart required.**
  `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W19: replace the stale known-RED allowlist example** (Lane: trivial
  config edit). `docs-reviewer.md` + `neovim_hub.md` now cite `explorer-open-searchbox`
  (the real known-RED) instead of `neovisual-explorer-open` (green). **Restart required.**
  `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W18: fix the "Load all four/three" off-by-one counts** (Lane: trivial
  config edit). `e2e-test-builder.md` (lists 4 → "all four"), `implementation-planner.md`
  (lists 3 → "all three"). The `uv tool install trailmark` fallback landed in the agent
  Trailmark sections during W3. The boilerplate de-dup is deferred (canonical fallback is
  in AGENTS.md). **Restart required.** `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W17: fix the pipeline strings** (Lane: trivial config edit). Added
  `debug-agent` + both `docs-reviewer` plan gates to `sprint-plan-gate/SKILL.md` step 4,
  `test-driven-development/SKILL.md`, and `command/hub.md`'s description. **Restart
  required.** `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W16: review hub only appends to progress.md** (Lane: trivial config edit).
  `neovim_review_hub.md` Hard constraints + Step 4 now state it is APPEND-ONLY and does not
  own `docs/progress.md`; `neovim_hub.md` states the same ownership boundary. **Restart
  required.** `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W15: reconcile the conventions handoff** (Lane: trivial config edit).
  `neovim_hub.md`'s Delegation contract now lists the real handoff inputs and states it
  does NOT re-send conventions; `e2e-test-builder.md` + `implementation-planner.md` now say
  the same (AGENTS.md auto-loaded, SKILL.md self-read). **Restart required.**
  `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W14: wire `code-slice-worker` into `arch-auditor`** (Lane: trivial config
  edit; user decision via `question`). Added a `code-slice-worker` `task` rule to
  `arch-auditor.md` + a "Large-slice offload" section (packet built via the
  `slicing-code-context` method); `neovim_review_hub.md` now notes the nested spawn.
  Requires `subagent_depth ≥ 2` (already set). Resolves A5. **Restart required.**
  `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W13: enforce the atomic GREEN commit policy** (Lane: trivial config
  edit). `neovim_hub.md` step 9's COMMIT block now requires one atomic commit for the
  whole change set, `check-doc-refs.ps1` PASS before committing, no "WIP … awaiting
  VERIFY" commit, no `Commit: <pending>` Done entry. **Restart required.**
  `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W12: reconcile progress.md's internal contradictions** (Lane: trivial
  config edit). Fixed: the `explorer-open-navigation` KNOWN-RED line (now GREEN, matching
  the Baseline/Done records); the severity line (now "filed subset 27 of 46:
  1 critical, 15 major, 11 minor"); the meta-review headers (now ALL DONE matching the
  COMPLETE note); the stale 42/21 F45 counts (annotated "at the time"). `check-doc-refs.ps1`
  PASS. Commit: (recorded below)
- **2026-09-27 — W11: stagger the final-gate unit run** (Lane: trivial config edit).
  `verification-agent.md` step 4 now runs the two unit projects staggered/sequentially
  (never simultaneous) per the recorded shared-`obj/` CS2012 lock note; treats a CS2012 as
  a lock flake. **Restart required.** `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W10: deviation adjudication on the verify path + DEVIATION return fields**
  (Lane: trivial config edit). `neovim_hub.md` gained a verify-time ADJUDICATE DEVIATIONS
  step (new 8b, before the re-plan which is now 8c); `debug-agent.md` returns a
  `DEVIATIONS FROM PLAN:` field; `verification-agent.md` returns a `DEVIATIONS:` slot.
  **Restart required.** `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W9: pass the missing handoff inputs** (Lane: trivial config edit).
  `neovim_hub.md`: step 3 passes the affected unit project(s); steps 5/6/8/8a pass the
  affected unit project(s) + the final-gate flag; step 4 passes the known-RED allowlist —
  matching the inputs `build-agent`/`debug-agent`/`verification-agent`/`implementation-planner`
  declare they need. **Restart required.** `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W8: encode the user feature-triage gate as a LOOP step** (Lane: trivial
  config edit). Added LOOP step **1f FEATURE-TRIAGE GATE** to `neovim_hub.md` (research
  LazyVim → check native VS reuse → ask the user build vs extend/reuse vs skip via the
  `question` tool → wait) and added it to the allowed-prompt list in the Delegation
  contract. **Restart required.** `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W7: reconcile the M-M4 model-pinning record** (Lane: trivial config
  edit). Marked M-M4 ❌ REVERTED/SUPERSEDED citing `4a2ec0b` (model pins removed; agents
  inherit the session default model) and narrowed the nested `trailmark-recon` spawn
  criterion to context isolation — the "cheaper model" rationale is void.
  `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W6: make the flaky-budget enforceable** (Lane: trivial config edit).
  `neovim_hub.md` step 8 now passes the cumulative per-scenario flaky counts to the
  verification-agent; the flaky-budget rule makes the HUB apply the 3rd-strike →
  REGRESSION upgrade; `verification-agent.md` input list + flaky policy now report count
  N+1 based on the hub-passed base. **Restart required.** `check-doc-refs.ps1` PASS.
  Commit: (recorded below)
- **2026-09-27 — W4+W5: unify the review-gate policy + define "iteration" once** (Lane:
  trivial config edit). `neovim_hub.md` gained a `## REVIEW-GATE POLICY` section (one
  post-REVISE rule for spec/2a/4a: hub fixes, 3-round cap, `question`-tool escalation on
  exhaustion) and a `## "Iteration" — defined once` section (an iteration = a RED→re-plan
  cycle from a real regression; doc-review rounds / flaky / known-RED / budget exhaustion
  never count). Removed the conflicting `:159` "counts toward the cap" phrase. **Restart
  required.** `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W3: drop no-signal Trailmark skills/passes from agent lists** (Lane:
  trivial config edit). Edited `.opencode/agent/{debug-agent,docs-reviewer,verification-agent,
  e2e-test-builder,implementation-planner,prompt-rule}.md`: removed `trailmark-finding-triage`,
  `trailmark-review-gate`, `graph-evolution` from the "Skills to use" lists (all no-signal —
  this VSIX has no entrypoints), replaced entrypoint-reach/taint guidance with
  `callers_of`/`callees_of`/`paths_between`/`reachable_from`, fixed the verification-agent
  self-contradiction, and scoped `prompt-rule.md` to this repo. **Restart required.**
  `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W2: commit the orchestration layer** (Lane: trivial config edit). The
  vendored Trailmark skill dirs, `.opencode/agent/code-slice-worker.md`,
  `.opencode/agent/trailmark-recon.md`, and the agent/skill/command/docs edits
  (A1–A9 integration) are now tracked, so a `git clone`/`git clean` cannot erase the
  workflow the review hub depends on (M-N3 regression). A8 updated to include
  `trailmark-recon.md`. No source under `MyExtension/`, `Telescope/`, `tests/` touched.
  `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W1: `bugfix-no-seam` sub-lane** (Lane: trivial config edit). Added a
  `bugfix (no-seam)` sub-lane to `.opencode/agent/neovim_hub.md` (mirroring the
  harness-only lane) and threaded it into LOOP step 3: when a diagnostic-neutral bug has
  no hermetic unit surface, RED is satisfied by re-confirming the named pre-existing
  known-RED scenario with ONE VS boot BEFORE BUILD (plus a stated no-unit-test reason);
  no such scenario ⇒ escalate, or upgrade to the feature lane if the fix adds a
  diagnostic/scenario. Closes the hole where the in-flight `explorer-open-searchbox` fix
  could reach BUILD with no RED. **Restart required** (agent files load at opencode
  startup). `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-19 — Explorer tree-select capability** (Lane: feature, attempt 1 GREEN
  after 1 VERIFY round): gave `SolutionExplorerController` a deterministic
  programmatic tree-selection action — `g` walks the Solution Explorer's DTE
  `UIHierarchy` (solution node → first project → first physical C# file), selects it
  via `UIHierarchyItem.Select` (no key injection → escapes the injected-key
  csproj-open trap), opens it directly (`ItemOperations.OpenFile`) and re-asserts
  the selection + tree focus for ~1.5s (defeats VS's SelectionPreview hover-timer
  focus steal). New diagnostic `[NeoVisual] solution-explorer select file=<full
  path>` (+ `select none`). New pure, unit-tested seam `HierarchyResolver`
  (`HierarchyNode` + `FirstSourceFilePath`, Kind-GUID classification). This greens
  the last-but-one known-RED e2e scenario `explorer-open-navigation`.
  **Change summary:** created `MyExtension/ToolWindows/HierarchyResolver.cs`; edited
  `MyExtension/ToolWindows/SolutionExplorerController.cs` (`Keys.G` action key +
  `SelectFirstSourceFile`/`HierarchyForestBuilder`/`FindFirstProjectNode`),
  `tests/NeoVisual.Tests/Program.cs` (Run_HierarchyResolver_FirstSourceFile +
  `Contains(Keys.G)` assertion), `tools/harness/test-e2e.ps1` (`explorer-open-navigation`
  scenario now presses `g`); docs synced (spec/AGENTS/SKILL/progress).
  **If this regresses, look first at `SolutionExplorerController.SelectFirstSourceFile`
  (the `Expanded = true` pre-walk + direct `OpenFile` + the 1.5s keeper) and the
  `solution-explorer select file=` diagnostic** — a `select none` means the project
  node was collapsed at walk time; no `editor-view-opened` matching the `select
  file=` path means the direct-open/keeper path broke.
  Doc sync: scenarios 31→32 passing / 2→1 known-RED, NeoVisual 25→26 across
  spec.md / AGENTS.md / SKILL.md; SPEC REVIEW gate APPROVED.
  Commit: `4f36fde`
- **2026-09-19 — 2 planned E2E coverage scenarios** (Lane: trivial, GREEN —
  partial item): added `telescope-open-file-searchbox` (insert-mode query →
  wait for settle → Enter opens the filtered single match, `Program.cs`) and
  `telescope-open-file-navigation` (Esc to normal, `j` moves selection to index
  1, Enter opens the MOVED-TO row `Service.cs` — pinned `results count=2
  selected=1`, tighter than the plan's `\d+`). Both assert ONLY existing
  diagnostics (M-M7 N/A). The other 2 planned scenarios (`explorer-open-navigation`,
  `explorer-open-searchbox`) FAILED against the current extension with real
  behavior gaps → registered as known-RED + queued as the next items (see the
  queue). No code change; no unit tests (pure coverage).
  **Change summary:** edited `tools/harness/test-e2e.ps1` (+2 scenarios + header).
  **If these regress, look first at the `opened file:` diagnostic + the
  `results count=N selected=M` pins.** Scenario count 29→33 registered (31
  passing + 2 known-RED) in spec.md / AGENTS.md / SKILL.md. Commit: `6933afc`.
- **2026-09-19 — Telescope implementation finder** (Lane: feature, attempt 1, GREEN):
  `ImplementationFinder` + `ImplementationHit` (Telescope, `Name="Implementation"`,
  `Space+F I`) list the **implementations/overrides of the symbol at the caret**
  (interfaces → implementing types/members, virtual/abstract → overrides,
  classes → derived) via Roslyn `SymbolFinder.FindImplementationsAsync`
  (MEF `VisualStudioWorkspace`, symbol-at-caret resolution mirroring the
  references gatherer). The gatherer maps each returned `ISymbol`'s FIRST
  in-source declaring location (skipping metadata symbols — NOT the references
  `foreach rs.Locations` verbatim) and **orders hits deterministically**
  `OrderBy(FilePath, OrdinalIgnoreCase).ThenBy(LineNumber)` so the type
  implementation (Shape.cs line 2) sorts before its member implementations
  (Shape.Draw line 4) — pinning the e2e's `line=2` assertions. Preview jumps to
  the implementation line; Enter opens at the line. New diagnostics:
  `implementations gathered count=N` and `opened implementation: file=... line=...`.
  New e2e scenario `telescope-implementation` (29th) passes the pinned chain
  `candidates=1 → count=1 → preview line=2 → opened line=2`; full suite GREEN
  (one pre-existing flake `neovisual-editor-insert`, retry-pass); units
  Telescope 56/56, NeoVisual 25/25; references + grep finders non-regressed.
  **Change summary:** created `Telescope/Finders/ImplementationHit.cs` +
  `Telescope/Finders/ImplementationFinder.cs`; edited `Telescope/Overlay/TelescopeOverlay.cs`
  (ImplementationHit preview branch), `MyExtension/Package/MyExtensionPackage.cs`
  (`GatherImplementations`/`OpenHitAtLine`/registration),
  `MyExtension/Input/InputHandler.cs` (ResolveAction case + `OpenTelescopeImplementation()`),
  `MyExtension/Resources/default-keybindings.json` (`F,I` → `telescope-implementation`),
  `tools/harness/test-e2e.ps1` (scenario + `Models/IShape.cs`/`Shape.cs` seeds in
  `$canonical`), `tests/Telescope.Tests/Program.cs` (+4 `Run_ImplementationFinder_*`).
  No DEVIATIONS.
  **If this regresses, look first at `GatherImplementations`' deterministic
  ordering (type-before-member) and the `opened implementation:` diagnostic** —
  the two places the e2e pins depend on. Doc sync: Telescope 52→56, scenarios
  28→29 across spec.md / AGENTS.md / SKILL.md. Commit: `b38b289`.
- **2026-09-19 — Telescope grep finder** (Lane: feature, attempt 1, GREEN):
  `GrepFinder` + `GrepHit` (Telescope, `Name="Grep"`, `Space+F G`) search the
  solution's project files for the typed query — **query-driven** via a new
  `IsQueryDriven` capability seam in the overlay (per-keystroke re-gather with a
  200ms debounce, skipping fzf for query finders; the fzf path for
  Files/Issues/References untouched — A6 verified). Empty query → 0 candidates;
  case-insensitive substring scan (`ProjectFiles.Enumerate`), `HitCap=200`;
  preview jumps to the hit line; Enter opens the file at the line. New
  diagnostics: `grep hits=N` (per-query gather summary) and
  `opened grep: file=... line=...`. New e2e scenario `telescope-grep` (28th)
  passes with the pinned chain `candidates=0 → hits=2 → preview line=4 →
  opened line=4` (seeded `GrepProbe.cs`, GREPME ×2); full suite GREEN (one
  pre-existing flake `neovisual-editor-insert`, retry-pass, count 2/3); units
  Telescope 52/52, NeoVisual 25/25.
  **Change summary:** created `Telescope/Finders/GrepHit.cs` +
  `Telescope/Finders/GrepFinder.cs`; edited
  `Telescope/Overlay/TelescopeOverlay.cs` (debounce + `RefreshQueryDrivenAsync` +
  GrepHit preview branch), `MyExtension/Input/InputHandler.cs`
  (ResolveAction case + `OpenTelescopeGrep()`), `MyExtension/Package/MyExtensionPackage.cs`
  (finder registration), `MyExtension/Resources/default-keybindings.json`
  (`F,G` → `telescope-grep`), `tools/harness/test-e2e.ps1` (scenario + `GrepProbe.cs`
  seed in `$canonical`), `tests/Telescope.Tests/Program.cs` (+6
  `Run_GrepFinder_*`). No DEVIATIONS.
  **If this regresses, look first at `TelescopeOverlay.RefreshResults`'s
  `IsQueryDriven` branch (debounce + `grep hits` summary) and `GrepFinder`
  `GetCandidates(string)`** — the two BP steps the e2e scenario asserts on.
  Doc sync: Telescope 46→52, scenarios 27→28 across spec.md / AGENTS.md /
  SKILL.md. Commit: `4faaf88`.
- **2026-09-19 — Telescope references finder** (Lane: feature, attempt 1, GREEN):
  `ReferencesFinder` + `ReferenceHit` (Telescope, `Name="References"`, `Space+F R`)
  lists every reference to the symbol at the caret with **read/write access**
  (Roslyn `SymbolFinder.FindReferencesAsync` via MEF-resolved
  `VisualStudioWorkspace`; `IsWrittenTo` via reflection — internal in Roslyn
  4.14); preview jumps to the reference line; Enter opens the file at the line.
  New diagnostics: `references gathered reads=N writes=N` and
  `opened reference: file=... line=... col=... access=read|write`. New e2e
  scenario `telescope-references` (27th) passes with `candidates=2 reads=1
  writes=1`; full suite GREEN (one pre-existing flake `neovisual-editor-insert`,
  retry-pass, count 1/3); units Telescope 46/46, NeoVisual 25/25.
  **Change summary:** created `Telescope/Finders/ReferenceHit.cs` +
  `Telescope/Finders/ReferencesFinder.cs`; edited `Telescope/Overlay/TelescopeOverlay.cs`
  (ReferenceHit preview branch), `MyExtension/Input/InputHandler.cs`
  (ResolveAction case + `OpenTelescopeReferences()`), `MyExtension/Package/MyExtensionPackage.cs`
  (finder registration + Roslyn gatherer `GatherReferences`/`OpenHitAtLine`),
  `MyExtension/Resources/default-keybindings.json` (`F,R` → `telescope-references`),
  `MyExtension/MyExtension.csproj` (+ `Microsoft.VisualStudio.LanguageServices`
  4.14.0, `ExcludeAssets="runtime"`), `tools/harness/test-e2e.ps1` (scenario + seeded
  `Models/Shared.cs`/`Reader.cs`/`Writer.cs` in `$canonical`),
  `tests/Telescope.Tests/Program.cs` (+4 `Run_ReferencesFinder_*`). 3 DEVIATIONS
  adjudicated ACCEPT (package version 4.14.0; `IsWrittenTo` via reflection;
  UI-thread guard for the offline test host) — no contract change.
  **If this regresses, look first at the `opened reference:` diagnostic +
  `ReferencesFinder.GetCandidates` (gather summary) and the
  `GlobalKeyboardHook`-independent overlay preview branch — the two BP steps the
  e2e scenario asserts on.** Doc sync: Telescope 42→46, scenarios 26→27 across
  spec.md / AGENTS.md / SKILL.md. Commit: `a623e09`.
- **2026-09-19 — Fix 4 known-RED backlog items / gate the 26-scenario suite green**
  (Lane: bugfix, attempt 1, GREEN): full 26-scenario e2e suite passes, no
  known-RED scenarios remain; NeoVisual.Tests 21→25 (4 new `Run_InjectedKeyGuard_*`),
  Telescope.Tests 42 unchanged. Three wrong harness assertions corrected
  (`prompt-motions` caret sequence, `open-file-normal` key=Return, `issues`
  results-count regex); the Enter-storm re-injection loop fixed with the pure
  `InjectedKeyGuard` (per-VK consume-once counter) wired into `KeyInjection.Press`
  + `GlobalKeyboardHook.HookCallback` (pass-through via `CallNextHookEx`, never
  re-handle an injected key) + a harness fail-fast (≤10 `solution-explorer open`
  lines post-baseline) + `Assert-VsFocused` in the explorer walk loops.
  **Change summary:** created `MyExtension/Hooks/InjectedKeyGuard.cs`; edited
  `MyExtension/Hooks/KeyInjection.cs` (Record first statement of Press),
  `MyExtension/Hooks/GlobalKeyboardHook.cs` (TryConsume at top of the key-down branch),
  `tools/harness/test-e2e.ps1` (3 assertion fixes + `Assert-NoEnterStorm` +
  Assert-VsFocused), `tests/NeoVisual.Tests/Program.cs` (+4 guard tests).
  **If this regresses, look first at `GlobalKeyboardHook.HookCallback`'s guard
  check (line ~107) and the `InjectedKeyGuard` counter semantics** — a swallowed
  injected Return means the guard consumed a key it shouldn't; a re-storm means
  the pass-through placement moved. No diagnostic format changed (M-M7 N/A).
  Doc sync: NeoVisual 21→25 in spec.md / AGENTS.md / SKILL.md; AGENTS.md
  scenario-status line now "all currently passing". Commit: `1606caf`.
- **2026-09-19 — Harness seeding hardening** (Lane: bugfix, attempt 1, GREEN):
  `tools/harness/test-e2e.ps1` now always resets the scratch solution
  (`Reset-ScratchSolution`) with uniform line endings; the "normalize line
  endings?" focus-steal modal is gone; `seed-reset` + `telescope-issues` e2e green;
  unit suites 42/21. Doc sync: 25→26 scenarios, 41→42 Telescope tests across
  spec.md / AGENTS.md / SKILL.md.
- **2026-09-19 — F45 log-prefix centralization** (Lane: feature, attempt 1, GREEN):
  `Telescope/Logging/DiagnosticLog.cs` single-sources the five log prefixes; all C# log
  sites + both harness scripts use it; `Run_LogPrefixes_Pinned` pins the contract;
  rg gates 0/1-per-prefix; e2e 19-scenario subset green (2 known-backlog assertion
  bugs excluded, not regressions).

## Architecture review backlog (from docs/reviews/architecture-review.md, 2026-09-19)

Findings approved for filing (user selection: "Everything incl. harness + tooling").
Full detail and the remaining report-only findings (F17-F21, F23-F35, F45) live in
`docs/reviews/architecture-review.md`. Severity of this **filed subset (27 items, of the 46
total findings)**: 1 critical, 15 major, 11 minor. (The remaining 19 findings —
F17-F21, F23-F35, F45 — stayed report-only.)

> **2026-09-28 — COVERED-BY the Architecture consolidation plan** (see the Pending
> queue first item + `docs/implementation_plan.md`): **F2, F3, F4, F6, F7, F11, F36,
> F37, F38, F39, F40, F41, F44, F46** — do NOT double-execute these; the consolidation
> plan resolves them. **Still open (NOT subsumed):** F5, F8, F9, F13, F14, F43
> (F22 + F12 + F15 were FIXED 2026-09-28 — see the Done section).
>
> **2026-10-02 — COVERED-BY the Code review fixes (51 findings) plan** (the FIRST
> pending item + `docs/implementation_plan.md`): **F5→R3, F7→R4, F8/F9→R22/R42,
> F10→R2, F14→R45** (documented/accepted n16) — do NOT double-execute these; the
> 51-findings plan resolves them. **F13/F43 — FIXED 2026-10-02** (verified already-fixed
> and annotated; the Architecture backlog is now CLOSED).

### Critical

1. **F1 — Enter-storm re-injection loop (supersedes known-bug #4).** ✅ FIXED 2026-09-19
   (backlog-fixes item; see Done section). `MyExtension/ToolWindows/SolutionExplorerController.cs:98`
   (+ `:137`), `MyExtension/Input/InputHandler.cs:283`, `MyExtension/Hooks/KeyInjection.cs:17` (stale "only
   inject arrows" doc). Injected Return re-entered the hook and re-triggered
   `OpenSelected()` → unbounded storm (~30x/100ms). Fixed with the in-flight guard:
   `InjectedKeyGuard` (per-VK consume-once counter, no clock) recorded in
   `KeyInjection.Press`; `GlobalKeyboardHook.HookCallback` passes matching key-downs
   through with `CallNextHookEx`. The `LLKHF_INJECTED` bail was rejected (the harness
   injects every test key via `keybd_event` — it would break all 26 scenarios).
   Harness fail-fast added (≤10 `solution-explorer open` lines post-baseline).

### Major

2. **F2 — Hook hot path violates the cheap pre-filter contract.**
   `MyExtension/Hooks/GlobalKeyboardHook.cs:116` — per-key `[Hook]` log runs
   `LogFileWriter` `File.AppendAllText` + pane write on the UI thread per interesting
   key. Fix: delete the per-key log (keep install/uninstall), or gate behind an env flag.
3. **F3 — Vim key→motion dispatch duplicated 4×.** `TextMotionHelper.cs:68` vs
   `TextInputToolWindowController.cs:181` (verbatim), `TelescopeOverlay.cs:561`
   (TryPromptMotion) vs `:599` (HandlePreviewKey). Fix: one shared Key→motion dispatch
   + log emitter over the single pure `TextMotionNavigator`.
4. **F4 — Block caret triplicated, drifted to black.** `TextInputToolWindowController.cs:330`
   paints `Brushes.Black`; `TextMotionHelper.cs:163` + `BlockCaretAdornment` paint white.
   Fix: single `ApplyCaretStyle` on `TextMotionHelper`; delete private brush; deactivate
   adornment on tool-window focus loss (F31 folded in).
5. **F5 — VimModeTracker stale buffer + latched failure.** `VimModeTracker.cs:159-165`
   focus loss never unsubscribes; `:307-316` trusts a stale buffer's `SwitchedMode`;
   `:457` `_resolved` latches on first failure. Fix: unsubscribe + null refs on focus
   loss; don't latch; route reflection failures through `NeoVisualLog`.
6. **F6 — WindowMatrix untestable + e2e-blind.** `WindowMatrix.cs:429` pipeline is
   COM-bound, no outcome diagnostic. Fix: pure `ReduceWindows(rects, active, direction)`
   state machine; log `[NeoVisual] navigate activated=...` / `no-op`.
7. **F7 — One unpaired frame kills ALL navigation.** `WindowControlAdapter.cs:39` throws
   E_FAIL; `WindowMatrix.cs:87` degrades everything to no-op. Fix: skip unpaired frames
   (yield only successful pairings).
8. **F8 — fzf spawned per keystroke with sync UI-thread pipe write.** `FzfFilter.cs:102-115`,
   `TelescopeOverlay.cs:339`. Fix: background the process I/O (`Task.Run`) + debounce in
   `RefreshResults`.
9. **F9 — fzf missing/crash is silent; `IsAvailable()` unused.** `FzfFilter.cs:133-136`,
   `:51`. Fix: call `IsAvailable()` at controller construction; log
   `[Telescope] fzf unavailable — filtering disabled`.
10. **F10 — Preview reload + re-tokenize per selection change.** `TelescopeOverlay.cs:402`.
    Fix: cache last preview payload (path + content hash + segments + navigator text).
11. **F11 — Finder seam doesn't scale to roadmap.** `TelescopeFinder.cs:14`,
    `TelescopeOverlay.cs:410-445` (payload-type hardcoded preview/line-jump; sync
    `GetCandidates`). Fix: preview/open seam on IFinder; async/lazy candidate gathering.
12. **F12 — Display-keyed payload lookup loses same-named duplicates.** `TelescopeOverlay.cs:357`
     → null-payload entries that silently do nothing. Fix: map filtered lines back by
     stable ordinal/id, not display text.
     ✅ **FIXED 2026-09-28** — `ResultMapper.MapBack` (see Done section).
13. **F13 — FileFinder re-implements the shared DTE walker.** `FileFinder.cs:116-190` vs
    `ProjectFiles.cs:31-103` near-verbatim. Fix: `GetCandidates` maps
    `ProjectFiles.Enumerate(dte)`.
    ✅ **FIXED 2026-10-02** — `FileFinder.GatherHits` now delegates to
    `ProjectFiles.Enumerate` (no private walker remains).
14. **F14 — Ctrl+N/P hijacked in every editor.** `PopupNavigation.cs:49` injects arrows
    with no popup-active check. Fix: re-add `ICompletionBroker.IsCompletionActive` gate.
15. **F15 — CodeIssuesFinder per-open DTE re-enumeration + full-file scans.**
     `CodeIssuesFinder.cs:71`. Fix: cache `ProjectFiles.Enumerate` per session in
     `TelescopeController` (invalidate on solution change); lazy/async TODO scan.
     ✅ **FIXED 2026-09-28** — `ProjectFileCache` + solution-change detection (see Done
     section; the lazy/async TODO scan half is deferred).
16. **F16 — Harness assertions for known-bugs 1-3 still wrong; no Enter-storm fail-fast.** ✅ FIXED 2026-09-19
    (backlog-fixes item; see Done section). Applied: `key=E caret=4` + extra `w` tap,
    `key=Return mode=normal handled=True`, `results count=\d+`, `Assert-NoEnterStorm`
    (≤10 `solution-explorer open` lines post-baseline) + `Assert-VsFocused` in the
    walk loop. (Overlaps known-bug backlog items 1-3 — supersedes those expectations.)

### Minor

17. **F22 — Leader state machine not extracted.** `InputHandler.cs:210` — routing logic
     needs AsyncPackage + MEF + WindowManager to construct. Fix: pure
     (proposed) `LeaderSequenceMatcher`/`SimpleKeyBuilder` (the `OverlayKeyHandler` pattern).
     ✅ **FIXED 2026-09-28** — `LeaderSequenceMatcher` extracted (see Done section).
18. **F36 — Test runner copy-paste between the two unit suites.**
    `tests/Telescope.Tests/Program.cs:26`, `tests/NeoVisual.Tests/Program.cs:29`. Fix:
    one shared `<Compile Include>` source file.
19. **F37 — iterate-telescope.ps1 stale doc + divergent close + triplicated helpers.**
     `tools/harness/iterate-telescope.ps1:6,312,84`. Fix: factor (proposed) `tools/harness/harness-common.ps1` or
     delete the script; fix header comment; reuse the Close-Telescope pattern.
20. **F38 — e2e runner ignores `$false` scenario returns.** `tools/harness/test-e2e.ps1:1092`.
    Fix: `$ok = $result -ne $false`.
21. **F39 — Wait-NewLogLine O(n²) re-reads.** `tools/harness/test-e2e.ps1:201` re-reads whole log
    per 300ms poll. Fix: `Get-Content -Tail` from baseline or cache last-read length.
22. **F40 — Failure output lacks log tail/step context.** `tools/harness/test-e2e.ps1:1095`. Fix:
    dump post-baseline log tail on failure; add step counter to assert messages.
23. **F41 — Send-Text maps punctuation to wrong VKs.** `tools/harness/test-e2e.ps1:92` (`!` →
    VK_PRIOR/PageUp). Fix: shift-chords or clipboard/SendKeys typing.
24. **F42 — Harness kills ALL devenv on failure.** `tools/harness/test-e2e.ps1:1105,1048`. Fix:
    kill only the spawned main/exp instance PIDs. ✅ **FIXED 2026-09-27 (M-M5)** —
    `$script:SpawnedVsPids` + `Stop-SpawnedVs`/`Stop-HarnessVs`; all blanket
    `Get-Process devenv | Stop-Process` kills removed.
25. **F43 — fzf unit test silently passes when fzf absent.** `tests/Telescope.Tests/Program.cs:150`.
    Fix: fail the test or count it as SKIP.
    ✅ **FIXED 2026-10-02** — the silent-pass test was replaced by explicit missing-fzf
    tests (`Run_FzfFilter_IsAvailableFalseForMissingPath`,
    `Run_FzfFilter_FilterAsyncSkipsSpawnWhenUnavailable`); the dead fzf-path resolver
    helper was removed.
26. **F44 — dte-command.ps1 hardcoded VS paths.** `tools/harness/dte-command.ps1:13`. Fix: vswhere
    fallback like the other two harness scripts.
27. **F46 — Host depends on "library" for core infra.** `MyExtension/MyExtension.csproj:31`
    (host uses `Telescope`'s `NeoVisualLog`/`TelescopeController`). Note for roadmap:
    state the seam explicitly so the pending finders don't grow more VS-coupled code
    inside the Telescope project.

## How the loop works

The authoritative loop is `neovim_hub.md` LOOP steps 1-11 (triage lanes → RED →
PLAN → BUILD → DEBUG → RE-PLAN → VERIFY → final gate → failure-log sweep → GREEN
commit). This file is the durable record the loop reads/writes; it does not
duplicate the loop definition.

## Agent-orchestration review backlog (meta-review, 2026-09-19)

Meta-review of `.opencode/agent/*.md`, `tools/lint/check-doc-refs.ps1`, and
`tools/harness/test-e2e.ps1` runner/bootstrap. Rechecked after the hub reinit: 0
invalidations, M-C1 downgraded. **Status: 5 major + 8 minor items — ALL DONE** (see
the COMPLETE note below; this header was stale).

### Major — status

1. **M-M1 — No wall-clock/token/cost budgets per delegation.** ✅ DONE
   `neovim_hub.md` Delegation contract now has per-step wall-clock budgets
   (PLAN/RE-PLAN ≤10m, BUILD ≤15m, DEBUG ≤15m, RED ≤25m, VERIFY ≤30m, DOCS REVIEW
   ≤10m) + a per-item cost cap; both escalate via `question` on exhaustion (reuses
   the subagent-crash fallback). Verified: budgets present, crash-fallback intact.
2. **M-M2 — 5-iteration cap gameable by flaky classification.** ✅ DONE
   Flaky-budget added to `neovim_hub.md` + `verification-agent.md`: a scenario
   classified FLAKY 3× within an item becomes a REGRESSION (feeds re-plan). Hub
   tracks `<scenario>: flaky x<N>` in the Execution Log.
3. **M-M3 — DEVIATIONS reported but never adjudicated.** ✅ DONE
   New hub step 6b (ADJUDICATE DEVIATIONS) before any RE-PLAN: ACCEPT → update plan
   + doc sync; REJECT → debug-agent reverts. Planner returns `DEVIATIONS RESOLVED:`.
   Recorded as `DEVIATION: <id> -> ACCEPT/REJECT` in the Execution Log.
4. **M-M4 — Models not pinned on 5 of 9 agents.** ❌ **REVERTED / SUPERSEDED
   (2026-09-27, W7).** The `model:` pins were deliberately removed in commit
   `4a2ec0b` ("Remove model pins from all agents (inherit session default model)") —
   `rg "model:" .opencode/agent` = 0. The decision reversed: agents inherit the session
   default model rather than pinning per-agent (pro/flash) models. **Consequence:** the
   "cheaper model" rationale once cited for the nested `trailmark-recon` spawn
   (`docs/progress.md`'s task-permission review `:673,683`) is **void** — nested spawning
   now adds an LLM turn with no model saving, so `arch-auditor → trailmark-recon` is
   justified only by context isolation, not cost. (Original claim — "pinned on all 9:
   pro on e2e-test-builder + docs-reviewer, flash on build-agent + debug-agent +
   arch-auditor" — no longer reflects the repo.)
5. **M-M5 — Every e2e run kills ALL devenv + full reboot per run.** ✅ DONE
   `tools/harness/test-e2e.ps1`: added `$script:SpawnedVsPids` + `Stop-SpawnedVs` + `Stop-HarnessVs`;
   all `Get-Process devenv | Stop-Process` blanket kills removed; exit kills now
   scoped to spawned PIDs; added `-NoBootstrap` reuse mode (discovers a booted
   instance, skips kill/reseed/main-VS/Debug.Start; guard fails fast when none
   running). Parse OK, -List 26 scenarios, tools-hash refreshed.

### Minor — status (all done; see the COMPLETE note below)

6. **M-C1 (downgraded from critical) — tools-hash is instruction-only, not enforced.** ✅ DONE
   `tools/harness/test-e2e.ps1` now has `Write-ToolsHash` (SHA-256 of every file under `tools/`)
   materialized at the bootstrap of every real run. **2026-09-28 update:** `log/tools-hash.txt`
   is allowlisted in `$runtimeArtifacts` (absent on a fresh clone is NOT drift — the hash's
   existence is enforced by `Write-ToolsHash` at every real run, not by the lint). `-List` stays
   side-effect-free (exits before the call).
7. **M-M6 — Final-gate unit suites run sequentially.** ✅ DONE
   `verification-agent.md` step 4 now runs BOTH unit projects CONCURRENTLY at the item's
   final gate (they're independent, no shared VS instance); loop-time single-project runs stay
   sequential (no benefit parallelizing one process). E2E stays serial.
8. **M-M7 — Lane triage is a flash judgment with an expensive failure mode.** ✅ DONE
   `neovim_hub.md` triage now has a **M-M7 HARD TRIGGER**: any plan that ADDS/CHANGES a
   `[Telescope]`/`[NeoVisual]` diagnostic line or a diagnostic-format contract forces the
   feature lane (full pipeline + e2e RED) regardless of apparent size — mechanical, not a
   judgment call.
9. **M-N1 — Count inconsistency in the review hub's own output.** ✅ DONE
   `docs/reviews/architecture-review.md:9` summary corrected to "1 critical, 15 major, 30 minor/nit
   (46 total)" matching the findings table (verified programmatically: 1/15/30=46, 46 distinct F-ids);
   `docs/progress.md` filed-subset count annotated "(of 46 total)".
10. **M-N2 — Review hub lacks the compaction re-pin guard.** ✅ DONE
    `neovim_review_hub.md` now has the same **Compaction re-pin (Compaction-Cliff guard)**
    as `neovim_hub.md` — after any compaction, re-read `docs/progress.md` + its own agent file
    and re-pin before continuing the audit.
11. **M-N3 — The entire orchestration layer is untracked.** ✅ DONE
    Committed `a37242e`: 38 orchestration-layer files (`.opencode/agent/*`, `.opencode/command/*`,
    13 `.opencode/skills/*` dirs, `docs/*`, `tools/*`, `tests/*`) now tracked. MyExtension/Telescope
    feature source deliberately left untracked (separate concern). A `git clean`/clone no longer
    loses the loop.
12. **M-N4 — Execution Log + append-only Build Plan accumulate per attempt.** ✅ DONE
    `neovim_hub.md` step 9 + `implementation-planner.md` Re-planning now trim each SUPERSEDED
    Execution-Log attempt to ONE line (verdict + `delegations:|VS boots:|iterations:` cost line),
    keeping only the latest attempt in full — the plan file stays lean across a multi-attempt item
    (durable record is `docs/progress.md`).
13. **M-N5 — "Read-only" verification still mutates the machine.** ✅ DONE
    `tools/harness/test-e2e.ps1` env vars switched from `'User'` to `'Process'` scope (main VS spawned by
    the script inherits them; no cross-session persistence), and a **Side effects** header note
    documents the run's writes (per-run logs, %TEMP% reseed, process-scope env vars, scoped
    devenv kills). Parse OK, no USER-scope writes remain.
14. **M-N6 — "Prompt rule (MANDATORY)" duplicated hub-to-hub.** ✅ DONE
    Created the single authoritative `.opencode/agent/prompt-rule.md`; both `neovim_hub.md` and
    `neovim_review_hub.md` now reference it with a one-line pointer instead of duplicating the rule
    inline. Lint passes (14 docs scanned, prompt-rule.md now part of the scanned set).

---
**Meta-review backlog COMPLETE (2026-09-19).** All 5 major + 8 minor items (M-M1..M-M5,
M-C1, M-M6, M-M7, M-N1..M-N6) fixed, verified, and recorded. Committed orchestration layer
`a37242e`; feature source (MyExtension/Telescope) was still untracked at that time (the
orchestration layer is now fully tracked as of `0490c41`, W2 2026-09-27).

## Trailmark + agent-infrastructure review (2026-09-27)

Verification that the vendored Trailmark skills and the hub/subagent wiring are usable on
this repo. **Verified working:** Trailmark 0.5.0 CLI + `uv run --with trailmark python -`
import; graph builds (`language="c_sharp"`, 1083 nodes / 2461 edges, parse 0.3s);
`preanalysis()` populates subgraphs; queries return precise `file:line` evidence; all 8
subagents are `question: deny` and the 4 read-only ones `edit: deny`; `command/hub.md`
and `command/review.md` point at the right agents; AGENTS.md test counts (56/26) match.

Findings + disposition (A-ids are local to this review; fixes to `.opencode/agent/*` and
`AGENTS.md` were applied directly with user permission):

1. **A1 (major) — proxy-mediated calls make `callers_of` silently return 0.** 485/1083
   nodes are `proxy.unresolved:*`; `callers_of("KeyInjection.Press")` = 0 while 6 in-repo
   callers exist (they attach to `proxy.unresolved:KeyInjection.Press`). ✅ FIXED — added
   a "Repo-specific traps" bullet to `AGENTS.md` + a pointer in `vs-extension-dev/SKILL.md`.
2. **A2 (major) — security passes vacuous + `trailmark diff` language trap.**
   `entrypoints=0` → taint / privilege-boundary / attack-surface / review-gate all empty;
   `trailmark diff` defaults `--language` to `python` and returns an empty diff on C#.
   ✅ FIXED — same trap bullet; `verification-agent.md` now pins `--language c_sharp` and
   drops review-gate/taint from its mandatory set.
3. **A3 (major) — review-hub slice lists stale.** 12 files missing, incl. all three new
    finders + the `TelescopeFinder.cs`/`IsQueryDriven` seams, `InjectedKeyGuard`,
   `HierarchyResolver`, `DiagnosticLog`, `check-doc-refs.ps1`. ✅ FIXED —
   `neovim_review_hub.md` slice lists updated + "enumerate at dispatch time" note.
4. **A4 (minor) — `prompt-rule.md` registered as a callable agent.** ✅ FIXED — added
   `disable: true` frontmatter (path unchanged; both hubs still reference it).
5. **A5 (minor) — `code-slice-worker` agent orphaned.** No hub/agent spawns it and
   `slicing-code-context` is unreferenced. ⏳ OPEN — decide: wire into `arch-auditor` for
   large slices, or delete.
6. **A6 (minor) — `vs-extension-dev/SKILL.md` had no Trailmark guidance.** ✅ FIXED —
   added a "Trailmark (structural queries)" section.
7. **A7 (nit) — review hub loaded `trailmark-finding-triage` (a security-triage skill)
   for architecture audits.** ✅ FIXED — dropped from `neovim_review_hub.md`.
8. **A8 (minor) — the new trailmark skills + `code-slice-worker.md` are untracked
   (`??`).** ✅ FIXED 2026-09-27 (W2) — committed the orchestration layer, including the
   vendored Trailmark skill dirs, `code-slice-worker.md`, and `trailmark-recon.md`
   (the prior list omitted the recon agent). A clone/`git clean` no longer loses them.
9. **A9 (major) — the trailmark integration broke the blocking doc-ref lint.** The
   committed HEAD had zero Trailmark references; the uncommitted integration added
   `.opencode/skills/trailmark*/` (a glob in backticks the linter treats as a literal
   path) to 10 docs + a backticked QueryEngine API name to AGENTS.md, so
   `pwsh tools/lint/check-doc-refs.ps1` exited **1 with 12 unresolved refs** — neovim_hub's
   hard pre-review gate would fail on the next item. ✅ FIXED — reworded the glob to the
   resolvable `.opencode/skills/trailmark` and un-backticked QueryEngine;
   lint now `[PASS] 15 docs scanned, 2745 refs, 0 unresolved`.
10. **A10 (major) — nested subagent spawning DOES work on 1.18.32; the first probe was
   invalid (no-restart confound).** The binary's task-tool guard is
   `while(b.parentID) h++; if (h >= (subagent_depth ?? 1)) fail("Subagent depth limit ...")`
   plus a child ruleset that adds `task:* deny` UNLESS the subagent has an explicit `task`
   rule. So hub→arch-auditor→trailmark-recon is allowed iff (a) `arch-auditor` has an
   explicit `task` rule AND (b) global `subagent_depth >= 2`. The earlier capability probe
   wrongly reported "no task tool" because agent files load only at STARTUP: the running
   session still had the PRE-EDIT `arch-auditor` (no `task` rule) and `trailmark-recon`
   was not yet a registered spawn target. ✅ RESOLVED — `task` rule restored on
   `arch-auditor` (scoped to `trailmark-recon` only); the hub also dispatches a whole-repo
   recon once (Step 1a) so auditors get a shared digest. ⚠️ **Fragility:** if
   `subagent_depth` is removed/lowered to 1, the nested spawn degrades silently (no `task`
   tool → the auditor runs the queries itself; the audit still completes). ⚠️ **Lesson
   (general):** agent/skill/config edits cannot be exercised in the same opencode session
   — you MUST restart before testing them, or the probe tests the stale definition. (A5
   corollary, corrected: `code-slice-worker`, if wired to an auditor, needs a `task` rule +
   `subagent_depth >= 2`, or hub-dispatch.)

### Spawn graph & required `subagent_depth` (verified post-restart 2026-09-27)

opencode's guard fails when the CALLER's ancestor count `h >= subagent_depth`. Verified
end-to-end after a restart: hub → arch-auditor → trailmark-recon succeeded.

| Level | Agent(s) | Spawns | h |
|---|---|---|---|
| L0 primary | `neovim_hub` | e2e-test-builder, implementation-planner, build-agent, debug-agent, verification-agent, docs-reviewer | 0 |
| L0 primary | `neovim_review_hub` | trailmark-recon (whole-repo), arch-auditor (per slice) | 0 |
| L1 subagent | `arch-auditor` | trailmark-recon (slice-scoped) | 1 |
| L1 leaf | build-agent, debug-agent, docs-reviewer, e2e-test-builder, implementation-planner, verification-agent, code-slice-worker | none (no `task` rule) | 1 |
| L2 leaf | `trailmark-recon` | none | 2 |

Deepest chain = **2 subagent levels**, so the required and sufficient `subagent_depth` is
**2** (the global config sets exactly 2). L1 `arch-auditor` (h=1) needs `depth > 1`; L2
`trailmark-recon` (h=2) is correctly blocked at `depth=2`. Do NOT lower to 1 (kills the
nested recon — it degrades to the hub-only Step 1a digest) and do NOT raise to ≥3 (no
chain needs it; it would let the recon leaf recurse). If `code-slice-worker` is wired:
hub-spawned = L1 (depth ≥1 suffices) or auditor-spawned = L2 (needs the `task` rule +
depth ≥2) — depth 2 covers both.

### Task-permission review — does any OTHER subagent need `task`? (2026-09-27)

Reviewed all 9 subagents. Verdict: **no other subagent benefits** — the only `task` rule
that earns its place is `arch-auditor` → `trailmark-recon`. Criterion: grant `task` only
when the child yields a standardized artifact several consumers share, or needs isolated
context — NOT to run a CLI query the caller can run itself. (Per W7 the "different/cheaper
model" clause is **void**: commit `4a2ec0b` removed all `model:` pins, so every spawn
inherits the session default model — a nested spawn never saves on model cost, only
context.) Every build/verify/plan agent already runs Trailmark inline via bash (parse
~0.2s), so spawning a recon subagent would add an LLM turn for no new information.
- build-agent, debug-agent, docs-reviewer, e2e-test-builder, implementation-planner:
  inline Trailmark queries suffice (each has a Trailmark section).
- verification-agent: its structural need is a before/after `trailmark diff`, which it can
  run itself — the `trailmark-recon` snapshot digest does not serve it.
- code-slice-worker, trailmark-recon: leaves by design (recon is the leaf of the chain).
All are L1 (spawned by a hub), so at `subagent_depth: 2` any of them COULD spawn L2 — the
constraint is value, not depth. Revisit only if context cost becomes the bottleneck: then
wire `code-slice-worker` (slicing-code-context) to offload bulky reads into an isolated
context, which is a context-reduction move rather than a Trailmark need (no model saving —
models are no longer pinned; see W7).

**New agent — `trailmark-recon` (added, per user request).** Read-only subagent that
builds the C# graph + `preanalysis()` and returns one compact `RECON:` digest (counts,
proxy share, empty entrypoint/taint passes, complexity hotspots, high-blast-radius count,
false-dead-code traps). Wired as the review hub's Step 1a: `neovim_review_hub.md` (a
PRIMARY agent) dispatches it ONCE per audit and passes the digest to every arch-auditor;
`arch-auditor.md` consumes the digest and MAY spawn `trailmark-recon` for a slice-scoped
digest (allowed by the explicit `task` rule + `subagent_depth >= 2`; see A10). This is the
single place the proxy / no-entrypoint caveats are applied.

> **Restart required:** agent/skill/config files are read once at opencode startup. Quit
> and relaunch opencode before A1–A4/A6/A7/A9 and the `trailmark-recon` wiring take effect.

## Workflow review backlog (meta-review, 2026-09-27) — `neovim_hub` + subagents

Full detail, evidence, and exact file:line references live in
`docs/reviews/architecture-review.md` (top section). Filed on 2026-09-27 with user approval
(selection "All findings (W1–W20)"; W21 filed on request). `neovim_hub` picks these one at a time via the
normal loop; W-ids are local to this review.

**Severity: 2 critical, 11 major, 7 minor, 1 nit.** The prior meta-review
(M-M1…M-N6, A1…A10) above already fixed most orchestration items; these are net-new
defects plus DONE/OPEN items whose record no longer matches the repo.

> ✅ **ALL W1–W21 FIXED (2026-09-27).** Each item below is annotated FIXED with a pointer
> to its `## Done` entry, in commits `8f71dbd` … `cc4f700`. W14 was a user decision (wire
> `code-slice-worker`); W18's boilerplate de-dup was deferred (low value — the canonical
> Trailmark fallback lives in AGENTS.md). **Restart opencode** for the agent/skill edits
> to take effect (agent files load once at startup; A10).

### Critical (fix first)

1. **W1 — `bugfix` lane has no valid RED path for a diagnostic-neutral bug with no
   unit seam.** ✅ **FIXED 2026-09-27** — added a `bugfix (no-seam)` sub-lane to
   `neovim_hub.md` (mirroring the harness-only lane) + threaded into LOOP step 3; RED
   is the named pre-existing known-RED scenario re-confirmed with ONE VS boot before
   BUILD. See the `## Done` entry.
2. **W2 — the orchestration layer is untracked and can be destroyed.** ✅ **FIXED
   2026-09-27** — committed the orchestration layer (Trailmark skills, `code-slice-worker.md`,
   `trailmark-recon.md`, agent/skill/command/docs) and updated A8 to include
   `trailmark-recon.md`. See the `## Done` entry.

### Major

3. **W3 — vacuous Trailmark skills prescribed + a self-contradiction.** ✅ **FIXED
   2026-09-27** — dropped `trailmark-finding-triage` (debug-agent), `trailmark-review-gate`
   (docs-reviewer, verification-agent), `graph-evolution` (verification-agent); replaced
   entrypoint-reach guidance with `callers_of`/`callees_of`/`paths_between`/`reachable_from`
   (e2e-test-builder, implementation-planner); fixed the verification-agent
   self-contradiction (skills list no longer loads the gate it later forbids); scoped
   `prompt-rule.md` away from taint/privilege and to this repo. Each edited Terrailmark
   section now also carries the `uv tool install trailmark` fallback.
4. **W4 — three gates, three post-REVISE policies.** ✅ **FIXED 2026-09-27** — added a
   single `## REVIEW-GATE POLICY` section in `neovim_hub.md` (hub fixes; 3-round cap;
   escalate via `question` on exhaustion) referenced by the spec gate, 2a, and 4a.
5. **W5 — the 5-iteration cap is counted two ways.** ✅ **FIXED 2026-09-27** — added a
   `## "Iteration" — defined once` section: an iteration is a RED→re-plan cycle from a
   real regression; doc-review rounds, flaky, known-RED, and budget exhaustion never
   consume the cap. 4a's "counts toward the cap" phrase removed.
6. **W6 — flaky-budget (M-M2) is inert.** ✅ **FIXED 2026-09-27** — step 8 now passes the
   cumulative per-scenario flaky counts into `verification-agent`; the HUB (not the fresh
   agent) performs the 3rd-strike → REGRESSION upgrade; the agent reports count N+1.
   `verification-agent.md` input list + flaky policy updated.
7. **W7 — M-M4 record is false.** ✅ **FIXED 2026-09-27** — M-M4 marked
   ❌ REVERTED/SUPERSEDED citing `4a2ec0b` (agents inherit the session default model);
   the "cheaper model" rationale for nested recon narrowed to context isolation only.
8. **W8 — the user feature-triage gate is not a hub step.** ✅ **FIXED 2026-09-27** —
   added LOOP step **1f FEATURE-TRIAGE GATE** (research LazyVim → check native VS reuse →
   ask the user build vs extend/reuse vs skip via `question` → wait for the decision) and
   added it to the Delegation contract's allowed-prompt list.
9. **W9 — incomplete delegation inputs.** ✅ **FIXED 2026-09-27** — step 3 now passes the
   affected unit project(s); steps 5/6/8/8a pass the affected unit project(s) + the
   final-gate flag; step 4 passes the known-RED allowlist — matching what
   `build-agent`, `debug-agent`, `verification-agent`, and `implementation-planner`
   require.
10. **W10 — deviation adjudication unreachable on the verify path.** ✅ **FIXED 2026-09-27** —
    added a verify-time ADJUDICATE DEVIATIONS step (8b) before the verify-time re-plan
    (8c), and added a `DEVIATIONS FROM PLAN:` field to `debug-agent.md`'s return format and
    a `DEVIATIONS:` slot to `verification-agent.md`'s return format.
11. **W11 — concurrent final-gate unit suites.** ✅ **FIXED 2026-09-27** —
    `verification-agent.md` step 4 now runs the two unit projects staggered/sequentially
    (never simultaneous), per the recorded shared-`obj/` CS2012 lock note; a CS2012 is
    treated as a lock flake (re-run once).
12. **W12 — records contradict reality.** ✅ **FIXED 2026-09-27** — the
    `explorer-open-navigation` KNOWN-RED line now reads GREEN (matching `:17`/`:124`);
    the severity line reads "filed subset (27 of 46): 1 critical, 15 major, 11 minor";
    the meta-review headers (`:506`/`:534`) now read ALL DONE matching the COMPLETE note;
    the F45 lines no longer carry the stale 42/21 counts (annotated "at the time" instead).
    (The code report's fixed-findings annotation was already refreshed.)
13. **W13 — GREEN commit is non-atomic.** ✅ **FIXED 2026-09-27** — `neovim_hub.md` step 9's
    COMMIT block now carries an **ATOMIC COMMIT POLICY**: one commit for the whole change
    set, `check-doc-refs.ps1` PASS before commit, no "WIP awaiting VERIFY" commit, no
    `Commit: <pending>` Done entry; hash-repair/lint-repair commits are a process failure.

### Minor

14. **W14** — `code-slice-worker` + `slicing-code-context` orphaned (A5 open).
    ✅ **FIXED 2026-09-27** — user chose to **WIRE it into `arch-auditor`**: added a
    `code-slice-worker` `task` rule to `arch-auditor.md` (+ a "Large-slice offload"
    section using the `slicing-code-context` packet), and referenced the nested spawn in
    `neovim_review_hub.md`. Requires `subagent_depth ≥ 2` (already set).
15. **W15** — handoff conventions mismatch.
    ✅ **FIXED 2026-09-27** — `neovim_hub.md`'s Delegation contract and both agent files
    (`e2e-test-builder.md`, `implementation-planner.md`) now say the same thing: the hub
    does NOT re-send conventions (AGENTS.md is auto-loaded; SKILL.md is read by the agent);
    only a step-specific convention not already covered is passed inline.
16. **W16** — both hubs claim `docs/progress.md` write access.
    ✅ **FIXED 2026-09-27** — `neovim_hub.md` now states the review hub does not own
    `docs/progress.md` (it appends filed findings only, on user approval); the review
    hub's Hard constraints + Step 4 now state append-only, never rewrite.
17. **W17** — pipeline-string drift.
    ✅ **FIXED 2026-09-27** — `sprint-plan-gate/SKILL.md` step 4, `test-driven-development/SKILL.md`
    line 9, and `command/hub.md`'s description now include `debug-agent` and the two
    `docs-reviewer` plan gates.
18. **W18** — off-by-one "Load all three/both". ✅ **FIXED 2026-09-27 (counts)** —
    `e2e-test-builder.md` now says "all four" (it lists 4), `implementation-planner.md`
    now says "all three" (it lists 3). The `uv tool install trailmark` fallback was added
    to the agent Trailmark sections in the same pass (W3). **Deferred:** de-duplicating the
    six boilerplate copies into one shared referenced file — the canonical fallback is
    already in AGENTS.md; a pure de-dup refactor is low-value next to the other findings.
19. **W19** — stale allowlist example.
    ✅ **FIXED 2026-09-27** — `docs-reviewer.md` and `neovim_hub.md` now cite the
    currently known-RED `explorer-open-searchbox` instead of the (now green)
    `neovisual-explorer-open`.

### Nit

20. **W20** — stale unit counts. ✅ **FIXED 2026-09-27** — `code-testing-agent/SKILL.md`
    now says 56/26 (with a drift caveat); the stale 42/21 in the F45/backlog sections was
    annotated "at the time" in W12. **Deferred nits:** `neovim_hub` still lists
    `dispatching-parallel-agents` although the build loop is serial (harmless — loaded only
    when fanning out review work); the Trailmark-review section was uncommitted, now
    committed by W2.

### Minor (filed on request)

21. **W21 - `docs/reviews/architecture-review.md` is not covered by the doc-ref lint.**
    ✅ **FIXED 2026-09-27** — added `docs/reviews/architecture-review.md` to
    `tools/lint/check-doc-refs.ps1`'s default `$Docs` set (17 docs scanned, 4110 refs,
    0 unresolved). Two lint gaps surfaced by the widened scope were handled: range refs
    (`file.cs:102-115`) now strip their `:N-M` suffix, and "code smells / recently
    removed / forbidden API" symbols (`IsCompletionActive`, `IsEmpty`, `Intersects`,
    `HashCode`, `MaxBy`, `MinBy`) are on the external allowlist. Every hub GREEN now
    re-scans the report, so W12-style drift is mechanically gated.

## Code review backlog (from docs/reviews/code-review.md, 2026-09-28)

> **PRIORITY (user directive 2026-09-28): architecture/consolidation fixes in this
> backlog — and the still-open Architecture review backlog above — are to be resolved
> FIRST, before any feature is added/resolved (the user-requested features 6-9 and the
> deferred fzf finder).** The consolidation cluster (M4/M5/M8/M9/M10/M11/M17 + the
> merge candidates in the report's "Consolidation plan") is the recommended first lane.

> **2026-09-28 — COVERED-BY the Architecture consolidation plan** (see the Pending
> queue first item + `docs/implementation_plan.md`): **M4, M5, M8, M9 + m17, m18, m19,
> m21, m23, m25, m33, m37, m38, m43, m44, m45 + n2, n3** — do NOT double-execute these.
> **Still open (NOT subsumed):** M19 + m6, m7, m8, m9, m10, m11, m12, m13, m16, m20,
> m22, m24, m32, m34, m35, m36, m46, m47, m48, m58, m59, m60, m62, m63, m64, m65 + n1,
> n4-n16.

Findings approved for filing (user selection: "All NEW findings"). Full detail lives in
`docs/reviews/code-review.md` (first run — the file did not exist before 2026-09-28). The F-ids
already filed in the Architecture review backlog above (F1-F16, F22, F36-F44, F46) are
NOT re-filed; the majors that re-confirm them (M1=F2, M2=F14, M3=F5, M6=F7, M7=F6,
M10=F3, M11=F4, M12=F8, M13=F9, M14=F10, M15=F11, M16=F12, M17=F13, M18=F15) stay in
the report only. Filed subset (59 items): 5 major, 38 minor, 16 nit.

### Major

1. **M4 — Six near-identical `OpenTelescope*` methods.** `InputHandler.cs:465,486,508,530,551`
   + `MyExtensionPackage.cs:410` — only the finder-name string differs. Fix: one
   `OpenTelescope(string finder)` called by `ResolveAction` + the command handler.
2. **M5 — Roslyn gatherers share ~90% prologue.** `MyExtensionPackage.cs:488` vs `:570`
   (`GatherReferences`/`GatherImplementations`); `:445`/`:466` openers byte-identical.
   Fix: shared `ResolveSymbolAtCaret()` + one `OpenHit(file, line)`.
3. **M8 — Redundant filter passes per navigation.** `WindowMatrix.cs:294`
   (RemoveWindowsByClosestAdjacency re-runs two filters already applied at :436-437).
   Fix: drop the inner calls. ✅ FIXED — N3's `WindowNavigationEngine.SelectTarget`
   runs the pipeline as ONE O(n) pass (each predicate applied once).
4. **M9 — TextInputToolWindowController re-implements ~60% of TextMotionHelper.**
   `TextInputToolWindowController.cs:223-340` (MotionName/FindFocusedTextBox/GetParent/
   CreateBlockBrush/GetAsyncKeyState private copies). Fix: delegate the WPF branch to
   `TextMotionHelper.TryMoveFocusedTextBox`; delete the private copies.
5. **M19 — GrepFinder full-solution scan synchronously on the UI thread per settled
   keystroke.** `GrepFinder.cs:52-98,153-175`. Fix: `Task.Run` the file scan; cache the
   file list; marshal only results back.

### Minor

6. **m6 — `_package` field unused.** `GlobalKeyboardHook.cs:49`. Drop it.
7. **m7 — `InitializeAsync` swallows all setup exceptions.** `MyExtensionPackage.cs:100-103`.
   Log the failing step via `NeoVisualLog`; install the hook last.
8. **m8 — `WindowManager` selection callback cast can throw; `guid != null` always true.**
   `WindowManager.cs:116-129`. Guard `value is int`; drop the null check.
9. **m9 — `IsTestStaleInjected()` does `File.Exists` per key on the hook path.**
   `WindowManager.cs:37-38`. Cache the sentinel result for the process lifetime.
10. **m10 — the package's first-.cs DTE walker was a third hand-rolled walker; removed in M22 —
    reuse `ProjectFiles.Enumerate`.**
11. **m11 — `IsWriteLocation` reflection failure silently downgrades to "read".**
    `MyExtensionPackage.cs:749-762`. Log a one-time warning when the property is missing.
12. **m12 — `_cachedTyping=false` set unconditionally on LostAggregateFocus.**
    `VimModeTracker.cs:189`. Guard with the `ReferenceEquals(_focusedView, view)` check.
13. **m13 — InjectedKeyGuard pending per-VK counter never expires.** `InjectedKeyGuard.cs:42-67`.
    Add a bounded TTL or clear-on-key-up.
14. **m16 — DPI divide uses system DPI, not per-monitor.** `WindowMatrix.cs:104`. Use
    per-monitor DPI.
15. **m17 — Same 4 screen-rect ints cached twice.** `IVsFrameView.cs:36,160`;
    `WindowControlAdapter.cs:20,47`. Expose one cached `RectCoordinate` from `IVsFrameView`.
16. **m18 — `.First()` + catch(InvalidOperationException) as control flow.**
    `WindowControlAdapter.cs:75`. Use `FirstOrDefault` + null check.
17. **m19 — IVsWindowFrameNotify members throw NotImplementedException.**
    `IVsFrameView.cs:129`. Drop the interface or implement no-ops. ✅ FIXED — the
    interface dies with `IVsFrameView` (N1 WindowAdapter).
18. **m20 — CheckDte redundant warm-up.** `WindowMatrix.cs:118`. Remove it. ✅ FIXED —
    the method was deleted by the Code-review findings (67) plan BP-18 (M9).
19. **m21 — DOWN filter magic `> 1` pixel tolerance.** `WindowMatrix.cs:385`. Name the
    constant / exclude the editor by identity.
20. **m22 — Dead null-activeWindow guard.** `WindowMatrix.cs:73`. Move the null check
    before GetLinkedWindowControlAdapters.
21. **m23 — `GetWindowScreenRect` return ignored.** `WindowControlAdapter.cs:56`. Log and
    skip on false.
22. **m24 — `CompareWindows` caption heuristic false-positive pairs.**
    `UtilityMethods.cs:112`. Compare by HWND/frame identity.
23. **m25 — `RectCoordinate` mutable public fields.** `RectCoordinate.cs:12`. Make readonly.
24. **m32 — ~1.5s `FocusKeeper` timer idiom duplicated.** `SolutionExplorerController.cs:337-358`
    vs `:125-154`. Extract a shared `FocusKeeper` helper.
25. **m33 — Editor-hosted insert motions (a/A/I) never clear the block caret.**
    `TextInputToolWindowController.cs:143-145`. Pass `styleCaret:true` / call
    `ApplyCaretStyle` after setting `_isInputMode`.
26. **m34 — `I`/`A` insert placements resolve to whole-buffer, not current-line.**
    `TextInputToolWindowController.cs:193-194`; `TextMotionNavigator.cs:207-210`. Fix in
    `TextMotionNavigator` (LineStart/current-line end).
27. **m35 — `FirstPathMatching` returns a folder's empty FilePath.**
    `SolutionExplorerController.cs:106-110`. Skip `PhysicalFolderKind` nodes with empty
    FilePath.
28. **m36 — `TryMove`/`ExecuteCommand` lack `ThrowIfNotOnUIThread()`.**
    `SolutionExplorerController.cs:182,470`. Add the guard.
29. **m37 — EditorFocusedVeto composed in InputHandler, outside the pure tested guard.**
    `FocusGuard.cs:30`. Move the veto composition into `FocusGuard`.
30. **m38 — Up to 3 focus-resolution attempts per key.** `TextInputToolWindowController.cs:129`.
    Order by the cheap `Keyboard.FocusedElement is IWpfTextView` check first.
31. **m43 — Key→motion dispatch triplicated (preview/prompt/tool-window).**
    `TelescopeOverlay.cs:653-719`; `TextMotionHelper.cs:51-101`. Extract a shared
    key→`TextMotion` dispatcher (same fix as M10).
32. **m44 — `GotoLine` byte-identical in two finders.** `CodeIssuesFinder.cs:122-137` vs
    `GrepFinder.cs:136-151`. Hoist into a shared helper.
33. **m45 — `File.AppendAllText` per Log call on the typing hot path.** `LogFileWriter.cs:82-102`.
    Buffer writes (kept-open StreamWriter, flush on timer/close) or throttle per-keystroke logs.
34. **m46 — `QuoteArg` naive Windows arg quoting.** `FzfFilter.cs:155-158`. Pass the query
    via stdin (`--query=-`) or use proper Windows arg quoting.
35. **m47 — Unterminated string ending in `\` throws `ArgumentOutOfRangeException`.**
    `SyntaxHighlighter.cs:196-208`. Clamp `i` to Length after the escape skip.
36. **m48 — `Dispose` swallows UI-thread assert, skips `CloseOverlay` off-thread.**
    `TelescopeController.cs:80-92`. Marshal the close to the UI thread before nulling.
37. **m58 — Unit tests reach real `keybd_event`.** `tests/NeoVisual.Tests/Program.cs:193-224`.
    Inject a fake `KeyInjection` seam so unit tests have no OS side effects.
38. **m59 — Fixed `Start-Sleep -Seconds 10` + per-key sleeps.** `tools/harness/test-e2e.ps1:2025`;
    `tools/harness/iterate-telescope.ps1:179`. Replace with bounded polls on a real signal.
39. **m60 — FzfFilter empty-query passthrough + missing-fzf fallback untested.**
    `tests/Telescope.Tests/Program.cs:148-164`. Add tests for both branches.
40. **m62 — TextMotionNavigator edge branches untested.** `tests/Telescope.Tests/Program.cs:303-409`.
    Add boundary tests (Down/Up at bounds, NextWord past end).
41. **m63 — spec.md §4 diagnostics contract omits asserted lines.** `docs/spec.md:153`
    (`open finder=`, `Focus prompt => True`, `results count=... selected=...`,
    `key=... mode=... handled=...`). Add them.
42. **m64 — Stale "AGENTS.md test counts (56/26)".** `docs/progress.md:940`. Update to 56/38.
43. **m65 — W12 fix claims architecture-review.md F1/F16/F45 annotated FIXED — they are not.**
    `docs/progress.md:1126`. Annotate them (or correct the claim).

### Nit

44. **n1 — `upperDistaneBound` typo.** `WindowMatrix.cs:345`.
45. **n2 — DefaultLogicalYWindowDivide (30) unused.** `CardinalNavigationConstants.cs:10`. ✅ FIXED — deleted in N4 (NavigationSettings).
46. **n3 — `ThrowOnFailure` inside the Next loop.** `IVsUIWindowFrameExtractor.cs:31`.
47. **n4 — Broken indentation.** `KeybindingConfig.cs:106`.
48. **n5 — Preview `Key.D4` triggers LineEnd without Shift.** `TelescopeOverlay.cs:705`.
49. **n6 — `ProjectFiles.Enumerate` lacks `ThrowIfNotOnUIThread()` self-assert.**
    `ProjectFiles.cs:15`.
50. **n7 — `CaretToPointer` walks whole FlowDocument per caret move.** `TelescopeOverlay.cs:811-843`.
51. **n8 — `telescope-open` re-asserts a line `Open-Telescope` already waited for.**
    `tools/harness/test-e2e.ps1:748-754`.
52. **n9 — `Run_RectCoordinate_StoresFields` coverage-touches a data holder.**
    `tests/NeoVisual.Tests/Program.cs:489-496`.
53. **n10 — Stale comment "DiagnosticLog.cs does not exist yet → RED".**
    `tests/Telescope.Tests/Program.cs:937`.
54. **n11 — Syntax braces assertion is a weak presence check.** `tests/Telescope.Tests/Program.cs:539`.
55. **n12 — Run_HierarchyResolver_FirstSourceFile eager test.** `tests/NeoVisual.Tests/Program.cs:236-269`.
56. **n13 — Stray form-feed + truncated commit hash `d18315`.** `docs/progress.md:49`.
57. **n14 — Garbled parenthetical about slnx project count.** `docs/spec.md:23`.
58. **n15 — Key-files table omits `Telescope/Logging/DiagnosticLog.cs`.** `docs/spec.md:84`.
59. **n16 — SKILL.md claims `coordinates` is "recomputed each access … live" — it's a
    construction-time snapshot.** `.opencode/skills/vs-extension-dev/SKILL.md`.
