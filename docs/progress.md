# Progress — MyExtension build queue

This file tracks the current build-loop state: known bugs, in-progress work, and
the pending feature queue. It is the working document the hub (`neovim_hub`)
reads at the start of every loop iteration.

> **Resume checkpoint:** the previous session checkpoint (`.opencode/PROGRESS.md`)
> has been superseded by this file.

## Baseline (as of last full verification)

- Offline units: `tests/Telescope.Tests` **56 passed**; `tests/NeoVisual.Tests`
  **25 passed**.
- Live E2E: `tools/test-e2e.ps1` lists **29 scenarios** (incl. `seed-reset`).
  The full 29-scenario suite is GREEN (verified 2026-09-19 after the
  implementation-finder item; one pre-existing flake `neovisual-editor-insert`
  passes on retry); **no known-RED scenarios remain**.

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

### New planned E2E scenarios (user-requested, NOT yet implemented)

- `telescope-open-file-searchbox` — Telescope: open a file directly from the
  search box (type a query in insert mode → Enter opens the filtered result).
- `telescope-open-file-navigation` — Telescope: open a file via navigation (Esc
  to normal mode, j/k to move selection, Enter opens). Partially overlaps
  `telescope-open-file-normal` — make the navigation variant actually move
  selection with j/k first.
- `explorer-open-navigation` — Solution Explorer: open a file via tree
  navigation (l/j/k to walk to a file, open with `o`).
- `explorer-open-searchbox` — Solution Explorer: open a file directly from the
  search box (i focuses it, type a query to filter the tree, open with `o`).
  Watch the search-box focus path and `TextMotionHelper` handling.

## In-progress

- **None.** (Most recent completions: harness seeding hardening and F45
  log-prefix centralization — see the Done section below.)

## F45 status (Item 1 — log-prefix centralization)

**COMPLETE** — code and e2e verified (see the Done section). `Run_LogPrefixes_Pinned`
passes, both unit suites green (42/21), all rg gates pass (C# literals = 0; harness
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
   investigation)~~ — **DONE**: full 26-scenario suite GREEN + both unit suites
   (25/42); see the Done section.
3. Implement the **Telescope finders** roadmap:
   - ~~`references` finder~~ — **DONE** (see Done section; `Space+F R`,
     read/write access, preview line-jump).
   - ~~`grep` finder~~ — **DONE** (see Done section; `Space+F G`, query-driven
     with a debounce, preview line-jump).
   - ~~`implementation` finder~~ — **DONE** (see Done section; `Space+F I`,
     Roslyn `FindImplementationsAsync`, preview line-jump).
   - `fzf` finder — with preview pane. **DEFERRED** (user clarified 2026-09-19:
     build implementation first; fzf-finder scope TBD by the user).
4. Add the 4 new planned E2E scenarios (above) and their offline unit tests.

## Done (durable completion history — appended on every GREEN)

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
`docs/architecture-review.md`. Severity (of 46 total): 1 critical, 15 major, 11 minor.

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
invalidations, M-C1 downgraded. **Status: 5 major done, 8 minor pending** (user
picks each fix or ignores; the hub implements + verifies, one item at a time).

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
4. **M-M4 — Models not pinned on 5 of 9 agents.** ✅ DONE
   `model:` pinned on all 9: pro on e2e-test-builder + docs-reviewer (judgment), flash
   on build-agent + debug-agent + arch-auditor. All frontmatter valid UTF-8.
5. **M-M5 — Every e2e run kills ALL devenv + full reboot per run.** ✅ DONE
   `tools/test-e2e.ps1`: added `$script:SpawnedVsPids` + `Stop-SpawnedVs` + `Stop-HarnessVs`;
   all `Get-Process devenv | Stop-Process` blanket kills removed; exit kills now
   scoped to spawned PIDs; added `-NoBootstrap` reuse mode (discovers a booted
   instance, skips kill/reseed/main-VS/Debug.Start; guard fails fast when none
   running). Parse OK, -List 26 scenarios, tools-hash refreshed.

### Minor — pending (next items)

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
`a37242e`; feature source (MyExtension/Telescope) still untracked.
