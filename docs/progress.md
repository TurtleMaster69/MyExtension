# Progress — MyExtension build queue

This file tracks the current build-loop state: known bugs, in-progress work, and
the pending feature queue. It is the working document the hub (`neovim_hub`)
reads at the start of every loop iteration.

> **Resume checkpoint:** the previous session checkpoint (`.opencode/PROGRESS.md`)
> has been superseded by this file.

## Baseline (as of last full verification)

- Offline units: `tests/Telescope.Tests` **56 passed**; `tests/NeoVisual.Tests`
  **26 passed**.
- Live E2E: `tools/test-e2e.ps1` lists **34 scenarios** (incl. `seed-reset`,
  `seed-leak`). **33 passing**; **1 known-RED**: `explorer-open-searchbox` (search-box
  focus-exit gap — queued as the next bugfix item; root cause in the queue
  below). `explorer-open-navigation` is **GREEN** (tree-select capability, `g`).
  The `neovisual-editor-insert` flake remains on record (retry-pass).

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
- ⚠️ **`explorer-open-searchbox`** — **KNOWN-RED** (registered; real gap; the ONE
  remaining known-RED): after
  `i`→type→`Esc`, `ExitInputMode`'s `View.SolutionExplorer` refocus does NOT
  restore tree focus — `o` falls through into the search box (no
  `solution-explorer open`). Needs the search-box focus-exit path fixed.
  **Queued as the next bugfix item.**

> Harness-health note (2026-09-19): the two test projects compile the shared
> `Telescope.csproj` into the same `obj/` path; Defender AV occasionally locks
> `Telescope.dll` (CS2012) on a simultaneous launch. A re-run passes; not a test
> failure. **Policy now staggered/sequential** (W11, `verification-agent.md` step 4)
> — do not launch both unit suites simultaneously.

## In-progress

- (none — the tree-select capability item reached GREEN 2026-09-19; see the Done
  section. Next up: `explorer-open-searchbox`, the remaining known-RED bugfix.)

## F45 status (Item 1 — log-prefix centralization)

**COMPLETE** — code and e2e verified (see the Done section). `Run_LogPrefixes_Pinned`
passes, all rg gates pass at the time of F45 (C# literals = 0; harness
raw literals = 1 each = the `$script:Pfx*` definitions; Pfx usage = 102 = exact
expected 91+4 + 3+4), and the 19-scenario e2e subset ran green (the only 2 failures
were known-backlog assertion bugs, not regressions).

## Pending queue (next items to pick)

Top of the queue, in priority order:

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
   - `fzf` finder — with preview pane. **DEFERRED** (user clarified 2026-09-19:
     build implementation first; fzf-finder scope TBD by the user).
4. ~~Add the 4 new planned E2E scenarios~~ — **PARTIAL**: `telescope-open-file-searchbox`
   + `telescope-open-file-navigation` **DONE** (see Done section); the other 2
   exposed real gaps → now the next queue items:
   - ~~**`explorer-open-navigation`**~~ — ✅ **DONE** (tree-select capability,
     GREEN 2026-09-19; see Done section).
   - **`explorer-open-searchbox`** — fix the search-box focus-exit gap
     (KNOWN-RED scenario registered). **← next item.**
5. **Telescope `fzf` finder** — **DEFERRED** (scope TBD by the user).

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

6. **Vim motions in the Solution Explorer search box.** (Partially exists — the
   search box already routes h/l/w/b/e/a/A/I via `TextMotionHelper` while a WPF
   TextBox is focused; confirm/extend the full motion set — e.g. `j`/`k`/`0`/`$`,
   block caret — to match the text-input tool-window surfaces. TBD: exact set.)
   Feature lane.

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
  bugfix/harness-only). `tools/test-e2e.ps1` now takes a SHA-256 snapshot of every seeded
  file at bootstrap (right after the fresh reseed → `log/seed-baseline.json`) and runs a
  new LAST scenario `seed-leak` that re-hashes the seeds at the END of the run and FAILS
  on any seeded file that was **added / removed / modified** during the run. Purpose: prove
  no e2e test wrote into a seeded file, so a real leak (a test that mutated a seed, or a
  future in-flight item that intends to) is caught rather than silently corrupting later
  runs. Expected/correct writes are excluded via an explicit `$AllowLeak` filename list
  (empty by default); the guard skips gracefully in `-NoBootstrap` reuse mode. Filesystem
  only — no keystrokes, no diagnostics (M-M7 N/A). Helpers `Get-SeedFiles` /
  `Write-SeedSnapshot` / `Assert-NoSeedLeak`; verified by a no-VS self-check (clean→pass,
  modify→fail, remove→fail). Scenario count 33→34 (32→33 passing) synced across
  spec.md / AGENTS.md / SKILL.md; `check-doc-refs.ps1` PASS. Commit: (recorded below)
- **2026-09-27 — W21: add docs/architecture-review.md to the doc-ref lint** (Lane: trivial
  config edit). `tools/check-doc-refs.ps1`: added `docs/architecture-review.md` to the
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
  `SelectFirstSourceFile`/`BuildForest`/`FindFirstProjectNode`),
  `tests/NeoVisual.Tests/Program.cs` (`Run_HierarchyResolver_FirstSourceFile` +
  `Contains(Keys.G)` assertion), `tools/test-e2e.ps1` (`explorer-open-navigation`
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
  **Change summary:** edited `tools/test-e2e.ps1` (+2 scenarios + header).
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
  **Change summary:** created `Telescope/ImplementationHit.cs` +
  `Telescope/ImplementationFinder.cs`; edited `Telescope/TelescopeOverlay.cs`
  (ImplementationHit preview branch), `MyExtension/MyExtensionPackage.cs`
  (`GatherImplementations`/`OpenImplementation`/registration),
  `MyExtension/InputHandler.cs` (ResolveAction case + `OpenTelescopeImplementation()`),
  `MyExtension/default-keybindings.json` (`F,I` → `telescope-implementation`),
  `tools/test-e2e.ps1` (scenario + `Models/IShape.cs`/`Shape.cs` seeds in
  `$canonical`), `tests/Telescope.Tests/Program.cs` (+4 `Run_ImplementationFinder_*`).
  No DEVIATIONS.
  **If this regresses, look first at `GatherImplementations`' deterministic
  ordering (type-before-member) and the `opened implementation:` diagnostic** —
  the two places the e2e pins depend on. Doc sync: Telescope 52→56, scenarios
  28→29 across spec.md / AGENTS.md / SKILL.md. Commit: `b38b289`.
- **2026-09-19 — Telescope grep finder** (Lane: feature, attempt 1, GREEN):
  `GrepFinder` + `GrepHit` (Telescope, `Name="Grep"`, `Space+F G`) search the
  solution's project files for the typed query — **query-driven** via a new
  `IQueryFinder` capability seam in the overlay (per-keystroke re-gather with a
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
  **Change summary:** created `Telescope/GrepHit.cs` +
  `Telescope/GrepFinder.cs` + `Telescope/IQueryFinder.cs`; edited
  `Telescope/TelescopeOverlay.cs` (debounce + `RefreshQueryDrivenAsync` +
  GrepHit preview branch), `MyExtension/InputHandler.cs`
  (ResolveAction case + `OpenTelescopeGrep()`), `MyExtension/MyExtensionPackage.cs`
  (finder registration), `MyExtension/default-keybindings.json`
  (`F,G` → `telescope-grep`), `tools/test-e2e.ps1` (scenario + `GrepProbe.cs`
  seed in `$canonical`), `tests/Telescope.Tests/Program.cs` (+6
  `Run_GrepFinder_*`). No DEVIATIONS.
  **If this regresses, look first at `TelescopeOverlay.RefreshResults`'s
  IQueryFinder branch (debounce + `grep hits` summary) and `GrepFinder`
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
  **Change summary:** created `Telescope/ReferenceHit.cs` +
  `Telescope/ReferencesFinder.cs`; edited `Telescope/TelescopeOverlay.cs`
  (ReferenceHit preview branch), `MyExtension/InputHandler.cs`
  (ResolveAction case + `OpenTelescopeReferences()`), `MyExtension/MyExtensionPackage.cs`
  (finder registration + Roslyn gatherer `GatherReferences`/`OpenReference`),
  `MyExtension/default-keybindings.json` (`F,R` → `telescope-references`),
  `MyExtension/MyExtension.csproj` (+ `Microsoft.VisualStudio.LanguageServices`
  4.14.0, `ExcludeAssets="runtime"`), `tools/test-e2e.ps1` (scenario + seeded
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
  **Change summary:** created `MyExtension/InjectedKeyGuard.cs`; edited
  `MyExtension/KeyInjection.cs` (Record first statement of Press),
  `MyExtension/GlobalKeyboardHook.cs` (TryConsume at top of the key-down branch),
  `tools/test-e2e.ps1` (3 assertion fixes + `Assert-NoEnterStorm` +
  Assert-VsFocused), `tests/NeoVisual.Tests/Program.cs` (+4 guard tests).
  **If this regresses, look first at `GlobalKeyboardHook.HookCallback`'s guard
  check (line ~107) and the `InjectedKeyGuard` counter semantics** — a swallowed
  injected Return means the guard consumed a key it shouldn't; a re-storm means
  the pass-through placement moved. No diagnostic format changed (M-M7 N/A).
  Doc sync: NeoVisual 21→25 in spec.md / AGENTS.md / SKILL.md; AGENTS.md
  scenario-status line now "all currently passing". Commit: `1606caf`.
- **2026-09-19 — Harness seeding hardening** (Lane: bugfix, attempt 1, GREEN):
  `tools/test-e2e.ps1` now always resets the scratch solution
  (`Reset-ScratchSolution`) with uniform line endings; the "normalize line
  endings?" focus-steal modal is gone; `seed-reset` + `telescope-issues` e2e green;
  unit suites 42/21. Doc sync: 25→26 scenarios, 41→42 Telescope tests across
  spec.md / AGENTS.md / SKILL.md.
- **2026-09-19 — F45 log-prefix centralization** (Lane: feature, attempt 1, GREEN):
  `Telescope/DiagnosticLog.cs` single-sources the five log prefixes; all C# log
  sites + both harness scripts use it; `Run_LogPrefixes_Pinned` pins the contract;
  rg gates 0/1-per-prefix; e2e 19-scenario subset green (2 known-backlog assertion
  bugs excluded, not regressions).

## Architecture review backlog (from docs/architecture-review.md, 2026-09-19)

Findings approved for filing (user selection: "Everything incl. harness + tooling").
Full detail and the remaining report-only findings (F17-F21, F23-F35, F45) live in
`docs/architecture-review.md`. Severity of this **filed subset (27 items, of the 46
total findings)**: 1 critical, 15 major, 11 minor. (The remaining 19 findings —
F17-F21, F23-F35, F45 — stayed report-only.)

### Critical

1. **F1 — Enter-storm re-injection loop (supersedes known-bug #4).** ✅ FIXED 2026-09-19
   (backlog-fixes item; see Done section). `MyExtension/ToolWindows/SolutionExplorerController.cs:98`
   (+ `:137`), `MyExtension/InputHandler.cs:283`, `MyExtension/KeyInjection.cs:17` (stale "only
   inject arrows" doc). Injected Return re-entered the hook and re-triggered
   `OpenSelected()` → unbounded storm (~30x/100ms). Fixed with the in-flight guard:
   `InjectedKeyGuard` (per-VK consume-once counter, no clock) recorded in
   `KeyInjection.Press`; `GlobalKeyboardHook.HookCallback` passes matching key-downs
   through with `CallNextHookEx`. The `LLKHF_INJECTED` bail was rejected (the harness
   injects every test key via `keybd_event` — it would break all 26 scenarios).
   Harness fail-fast added (≤10 `solution-explorer open` lines post-baseline).

### Major

2. **F2 — Hook hot path violates the cheap pre-filter contract.**
   `MyExtension/GlobalKeyboardHook.cs:116` — per-key `[Hook]` log runs
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
13. **F13 — FileFinder re-implements the shared DTE walker.** `FileFinder.cs:116-190` vs
    `ProjectFiles.cs:31-103` near-verbatim. Fix: `GetCandidates` maps
    `ProjectFiles.Enumerate(dte)`.
14. **F14 — Ctrl+N/P hijacked in every editor.** `PopupNavigation.cs:49` injects arrows
    with no popup-active check. Fix: re-add `ICompletionBroker.IsCompletionActive` gate.
15. **F15 — CodeIssuesFinder per-open DTE re-enumeration + full-file scans.**
    `CodeIssuesFinder.cs:71`. Fix: cache `ProjectFiles.Enumerate` per session in
    `TelescopeController` (invalidate on solution change); lazy/async TODO scan.
16. **F16 — Harness assertions for known-bugs 1-3 still wrong; no Enter-storm fail-fast.** ✅ FIXED 2026-09-19
    (backlog-fixes item; see Done section). Applied: `key=E caret=4` + extra `w` tap,
    `key=Return mode=normal handled=True`, `results count=\d+`, `Assert-NoEnterStorm`
    (≤10 `solution-explorer open` lines post-baseline) + `Assert-VsFocused` in the
    walk loop. (Overlaps known-bug backlog items 1-3 — supersedes those expectations.)

### Minor

17. **F22 — Leader state machine not extracted.** `InputHandler.cs:210` — routing logic
     needs AsyncPackage + MEF + WindowManager to construct. Fix: pure
     (proposed) `LeaderSequenceMatcher`/`SimpleKeyBuilder` (the `OverlayKeyHandler` pattern).
18. **F36 — Test runner copy-paste between the two unit suites.**
    `tests/Telescope.Tests/Program.cs:26`, `tests/NeoVisual.Tests/Program.cs:29`. Fix:
    one shared `<Compile Include>` source file.
19. **F37 — iterate-telescope.ps1 stale doc + divergent close + triplicated helpers.**
     `tools/iterate-telescope.ps1:6,312,84`. Fix: factor (proposed) `tools/harness-common.ps1` or
     delete the script; fix header comment; reuse the Close-Telescope pattern.
20. **F38 — e2e runner ignores `$false` scenario returns.** `tools/test-e2e.ps1:1092`.
    Fix: `$ok = $result -ne $false`.
21. **F39 — Wait-NewLogLine O(n²) re-reads.** `tools/test-e2e.ps1:201` re-reads whole log
    per 300ms poll. Fix: `Get-Content -Tail` from baseline or cache last-read length.
22. **F40 — Failure output lacks log tail/step context.** `tools/test-e2e.ps1:1095`. Fix:
    dump post-baseline log tail on failure; add step counter to assert messages.
23. **F41 — Send-Text maps punctuation to wrong VKs.** `tools/test-e2e.ps1:92` (`!` →
    VK_PRIOR/PageUp). Fix: shift-chords or clipboard/SendKeys typing.
24. **F42 — Harness kills ALL devenv on failure.** `tools/test-e2e.ps1:1105,1048`. Fix:
    kill only the spawned main/exp instance PIDs.
25. **F43 — fzf unit test silently passes when fzf absent.** `tests/Telescope.Tests/Program.cs:150`.
    Fix: fail the test or count it as SKIP.
26. **F44 — dte-command.ps1 hardcoded VS paths.** `tools/dte-command.ps1:13`. Fix: vswhere
    fallback like the other two harness scripts.
27. **F46 — Host depends on "library" for core infra.** `MyExtension/MyExtension.csproj:31`
    (host uses `Telescope`'s `NeoVisualLog`/`TelescopeController`). Note for roadmap:
    state the seam explicitly so the pending finders don't grow more VS-coupled code
    inside the Telescope project.

## How the loop works

One item at a time: write `docs/implementation_plan.md` (with a known-RED
allowlist) → RED (e2e-test-builder, right-reason RED) → Build Plan
(implementation-planner) → BUILD (build-agent) → VERIFY (verification-agent, with
harness-health checks + failure classification known-RED / flaky / regression) →
DEBUG (debug-agent, verify-time) → RE-PLAN (implementation-planner) → max 5
iterations counting real regressions only (flaky/known-RED don't count); escalate
to the user via `question` on identical-repeat or after the cap. **GREEN** →
append a durable `## Done` entry to this file, sync `docs/spec.md` + `AGENTS.md` +
SKILL.md when counts/features changed, and re-run the SPEC REVIEW gate. See
`neovim_hub.md` LOOP steps 1-10 for the authoritative flow.

## Agent-orchestration review backlog (meta-review, 2026-09-19)

Meta-review of `.opencode/agent/*.md`, `tools/check-doc-refs.ps1`, and
`tools/test-e2e.ps1` runner/bootstrap. Rechecked after the hub reinit: 0
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
   `tools/test-e2e.ps1`: added `$script:SpawnedVsPids` + `Stop-SpawnedVs` + `Stop-HarnessVs`;
   all `Get-Process devenv | Stop-Process` blanket kills removed; exit kills now
   scoped to spawned PIDs; added `-NoBootstrap` reuse mode (discovers a booted
   instance, skips kill/reseed/main-VS/Debug.Start; guard fails fast when none
   running). Parse OK, -List 26 scenarios, tools-hash refreshed.

### Minor — status (all done; see the COMPLETE note below)

6. **M-C1 (downgraded from critical) — tools-hash is instruction-only, not enforced.** ✅ DONE
   `tools/test-e2e.ps1` now has `Write-ToolsHash` (SHA-256 of every file under `tools/`)
   materialized at the bootstrap of every real run; `tools/check-doc-refs.ps1` no longer
   allowlists `log/tools-hash.txt` (removed from `$runtimeArtifacts`), so a genuinely missing
   hash now fails the lint (verified: present→PASS, absent→exit 1). `-List` stays side-effect-free
   (exits before the call).
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
   `docs/architecture-review.md:9` summary corrected to "1 critical, 15 major, 30 minor/nit
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
    `tools/test-e2e.ps1` env vars switched from `'User'` to `'Process'` scope (main VS spawned by
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
   finders + the `TelescopeFinder.cs`/`IQueryFinder` seams, `InjectedKeyGuard`,
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
   `pwsh tools/check-doc-refs.ps1` exited **1 with 12 unresolved refs** — neovim_hub's
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
`docs/architecture-review.md` (top section). Filed on 2026-09-27 with user approval
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

21. **W21 - `docs/architecture-review.md` is not covered by the doc-ref lint.**
    ✅ **FIXED 2026-09-27** — added `docs/architecture-review.md` to
    `tools/check-doc-refs.ps1`'s default `$Docs` set (17 docs scanned, 4110 refs,
    0 unresolved). Two lint gaps surfaced by the widened scope were handled: range refs
    (`file.cs:102-115`) now strip their `:N-M` suffix, and "code smells / recently
    removed / forbidden API" symbols (`IsCompletionActive`, `IsEmpty`, `Intersects`,
    `HashCode`, `MaxBy`, `MinBy`) are on the external allowlist. Every hub GREEN now
    re-scans the report, so W12-style drift is mechanically gated.
