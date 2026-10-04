# Plan — Gap 6 (core): goto commands — `gd`/`gI`/`gr` → single-hit direct, multi-hit Telescope

> **Lane: feature (e2e ENABLED).** UPDATE 2026-10-04 (user): the e2e-deferral mandate is
> LIFTED (the machine supports the VS Experimental Instance) — the e2e scenario specified here
> is CREATED + PROVEN RED by `e2e-test-builder` before the build and EXECUTED by
> `verification-agent` at VERIFY. New capability + likely one new diagnostic → **M-M7 trigger**
> (feature lane, full pipeline).
>
> **Source:** the queue's Gap 6 item ("goto-definition finder + wire VsVim gd/gr/gi to the
> Telescope finders") + the user's 2026-10-04 instructions: *"remap go to definition go to
> reference go to implementation to gd, gI, gr … if it has only 1 hit go directly there if its
> multiple hits forward it to telescope and display it there"* + the wiring decision:
> *"since this is only gonna work in vsvim context text windows its probably better that I do
> it there and just map extension command there — before I just mapped default visual studio
> commands to them and it worked."*
>
> **WIRING DECISION (user-made):** the extension exposes **VS commands**; the USER maps
> `gd`/`gI`/`gr` in **VsVim** themselves (they have mapped VS commands in VsVim before — it
> works). NO leader keys, NO hook changes, NO g-sequence state machine, NO VsVim interop.
> The companion plan (`plans/plan.md` — Telescope columns + preview-as-editor) is unaffected;
> that plan's multi-hit display is where this plan's "forward to telescope" lands.
>
> **Ground truth:** Gap 3 in flight (neovim_hub); the baselines move under it (NeoVisual
> 177→187). This plan's arithmetic is taken against the ACTUAL totals at execution time.
> The repo already has: `ReferencesFinder` + `ImplementationFinder` (caret-symbol Roslyn
> gatherers), `RoslynGatherers.TryGetCaretSymbol` (the shared caret-symbol seam),
> `TelescopeCommand.cs` (the VS command wiring for the Telescope finders — the exact pattern
> this plan extends), `HitOpener` (open-at-line), `TelescopeLauncher.FinderNames` (the finder
> registry that drives the derived `Actions.Registry` entries).

## Goal

Three VS commands — **goto-definition**, **goto-references**, **goto-implementation**
(Telescope-backed) — each gathering the targets for the symbol at the caret: **exactly 1 hit →
open it directly** (no overlay); **multiple hits → open the Telescope overlay** with the
corresponding finder. The user wires `gd`/`gr`/`gI` to these commands in VsVim.

## Approach

### D1 — The three VS commands (the TelescopeCommand pattern)

Read `MyExtension/Package/Utils/TelescopeCommand.cs` (the existing VS command wiring for the
Telescope finders) + its .vsct registration; add three commands following the SAME pattern.
The planner pins: the exact canonical command names (DTE `ExecuteCommand` form — derived from
the package's command set + the VSCT symbols; cite how the existing finder commands are
named), the command flags, and the visibility context (a text editor / any code window).

### D2 — The single/multi-hit dispatcher (pure seam)

New pure type (the `CloseWindowCommand`/`DiagnosticNavigator` pattern):
`GotoDispatcher.Decide(int hitCount)` → `DirectJump | OpenOverlay` (1 → direct; 0 and >1 →
overlay? **Pin: 0 hits → OpenOverlay too** (the overlay shows the empty state + the user can
retype) — or a no-op diagnostic? The planner picks + pins; LazyVim's reference: an empty
result list is shown, not a silent no-op). The direct-jump path uses the existing
`HitOpener` open-at-line; the overlay path uses `TelescopeLauncher.Open(finderName)`.

### D3 — The goto-definition finder (NEW — the Gap 6 build)

New `DefinitionFinder` (Telescope, `Name="Definition"`): the caret symbol's DEFINITION
locations via Roslyn — the ReferencesFinder/ImplementationFinder pattern (MEF-resolved
`VisualStudioWorkspace` + `RoslynGatherers.TryGetCaretSymbol`; the definition locations from
the symbol's `DeclaringSyntaxReferences` / `SymbolFinder.FindSourceDefinitionAsync` — the
planner pins the exact API from the RoslynGatherers source + the existing gatherers'
host-injected seam so it stays hermetic-testable). Deterministic ordering
(`OrderBy(FilePath).ThenBy(LineNumber)` — the ImplementationFinder precedent). Registered in
`MyExtensionPackage` + `TelescopeLauncher.FinderNames` (which auto-derives the
`telescope-definition` registry entry — keeping `Run_ActionsRegistry_TelescopeMapsToFinder`
green; NO default leader binding is added unless the user asks).

### D4 — The command actions

Each command: resolve the caret symbol (the existing seam) → gather (references/
implementations/definitions) → `GotoDispatcher.Decide(count)` → direct-jump (open at line +
log) or `TelescopeLauncher.Open(finder)` (the finder re-gathers at overlay open — the
double-gather is acceptable; the planner may instead pass the pre-gathered hits if the
overlay supports it — decide + justify).

### D5 — Diagnostics (M-M7)

The direct-jump path needs an outcome diagnostic — pin ONE new literal family, e.g.
`[Telescope] goto-direct finder=... file=... line=...` (or reuse the finders' existing
`opened reference:`/`opened implementation:`/`goto line=` lines where they already fire —
the planner pins the minimal set; every new literal is declared for the log-literal diff gate).

### D6 — The VsVim mapping (docs — the user's own step)

The docs record the three canonical command names + a worked VsVim mapping example (the
user's established flow — e.g. mapping the command to `gd`/`gr`/`gI` in VsVim's keyboard
options / vimrc). No code.

### D7 — Tests + harness + docs

- **Unit (`tests/Telescope.Tests` + possibly NeoVisual for the registry count):** the
  `GotoDispatcher` seam (RED CS0246), the DefinitionFinder's pure gather/mapping parts (the
  host-injected seam pattern), the registry count (the FinderNames entry auto-derives
  `telescope-definition` — the telescope-maps-to-finder test stays green; the count test
  updates if it pins an exact number).
- **Harness:** a queued scenario — the commands are executable via the harness's DTE helper
  (`dte-command.ps1` can execute a VS command), so the scenario can invoke each command on a
  seeded symbol and assert the direct-jump/overlay diagnostics. The scenario is CREATED +
  PROVEN RED by `e2e-test-builder` before the build and EXECUTED at VERIFY (the e2e-ENABLED
  lane).
- **Docs:** spec.md/AGENTS.md/SKILL.md (the commands + the finder + the VsVim mapping note),
  progress.md (the Gap 6 item → partially DONE: the goto core; the remaining Gap 6 scope —
  any additional gd/gr/gi polish — stays queued).

## Acceptance criteria (each mapped to a diagnostic + a test)

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | The goto-definition command with 1 hit opens the definition directly | the pinned direct-jump literal | unit `Run_GotoDispatcher_SingleHitIsDirectJump`; e2e `telescope-goto` Part 1 (executed at VERIFY) |
| AC2 | The goto-definition command with multiple hits opens the Telescope overlay with the Definition finder | the existing `open finder=` line | unit dispatcher + finder tests; e2e `telescope-goto` Part 2 (executed at VERIFY) |
| AC3 | The goto-references command behaves identically over the EXISTING References finder | existing `references gathered`/`opened reference:` lines | unit dispatcher; e2e `telescope-goto` Part 3 (executed at VERIFY) |
| AC4 | The goto-implementation command behaves identically over the EXISTING Implementation finder | existing `implementations gathered`/`opened implementation:` lines | unit dispatcher; e2e `telescope-goto` Part 4 (executed at VERIFY) |
| AC5 | The three commands are registered and DTE-executable | the command names resolve | unit registry/wiring tests; e2e (executed at VERIFY) |
| AC6 | The Definition finder's results are deterministic + hermetic-testable | `open finder=Definition candidates=...` | unit `Run_DefinitionFinder_*` |

## Files to be touched (initial estimate)

- **Modified:** `MyExtension/Package/Utils/TelescopeCommand.cs` (+ its .vsct),
  `MyExtension/Package/MyExtensionPackage.cs` (register), `MyExtension/Package/Utils/TelescopeLauncher.cs`
  (FinderNames), `tests/Telescope.Tests/Program.cs`, `tests/NeoVisual.Tests/Program.cs`
  (the registry count — ONLY after Gap 3 lands; coordinate), `docs/spec.md`, `AGENTS.md`,
  `SKILL.md`, `docs/progress.md`, the harness.
- **Created:** `Telescope/Finders/DefinitionFinder.cs` (+ its hit model), the pure
  `GotoDispatcher` seam.
- **Not touched:** the hook (`GlobalKeyboardHook`), `InputHandler` (no new key routing — the
  commands are DTE-executed), `LeaderSequenceMatcher`/`KeyNames` (no leader keys).

## Open risks / uncertainty

1. **The canonical command names (low).** Pinned from the existing TelescopeCommand pattern;
   the user maps them in VsVim — a name change after mapping is a user-side edit (documented).
2. **The definition gather's shape (medium).** `DeclaringSyntaxReferences` vs
   `SymbolFinder.FindSourceDefinitionAsync` — the planner pins from the RoslynGatherers
   source; metadata-only symbols (no source location) are skipped (the ImplementationFinder
   precedent).
3. **Gap 3 coordination (process).** The NeoVisual registry-count test moves under Gap 3
   (12→16); this plan adds a FinderNames entry (16→17 after Gap 3) — the planner sequences
   the count-test edits against the ACTUAL post-Gap-3 state.
4. **The double-gather (low).** The command gathers to count, then the overlay re-gathers —
   acceptable (the gatherers are cached); the planner may optimize later.

## Build Plan

> **Aggregated (Stage 3)** from `artifacts/goto-section-{a,b,c}.md` — the authoritative full
> detail (complete code, the .vsct content, the assertion tables, the doc edits) lives there;
> the steps below are the executable contract. **e2e is ENABLED** (the scenario is created +
> proven RED before the build, executed at VERIFY).
>
> **Pinned corrections (binding — the plan's D1/D2 guesses are superseded):** (1) **NO .vsct
> exists** — `Telescope.Show` is registered dynamically/unnamed, so this plan ADDS
> `MyExtensionPackage.vsct` (+ `VSCTCompile` + `[ProvideMenuResource("Menus.ctmenu", 1)]`);
> (2) the canonical command names are **`MyExtension.GotoDefinition` / `MyExtension.GotoReferences` /
> `MyExtension.GotoImplementation`** (hyphenated names are invalid — VS strips hyphens;
> PascalCase per the VS command-name rules); flags `CommandWellOnly`, NO visibility
> constraints (`ExecuteCommand` fails on a disabled command); (3) **0 hits → OpenOverlay**
> (LazyVim parity — the overlay shows the empty state); (4) the Roslyn API =
> **`DeclaringSyntaxReferences`** (synchronous; no `FindSourceDefinitionAsync` hop) +
> `Locations.Where(IsInSource)` fallback + (path,line) dedupe + **finder-side**
> `OrderBy(FilePath).ThenBy(LineNumber)` (in `DefinitionFinder.GatherHits` — the determinism
> test requires finder-side ordering); (5) the double-gather (command counts, the overlay
> re-gathers) is ACCEPTED; (6) the registry is ALREADY 16 (Gap 3 landed) → **16 → 17**;
> (7) **M-M7 = 6 new byte-exact literals**: `[Telescope] definitions gathered count=`,
> `[Telescope] opened definition:`, `[Telescope] goto-direct finder=… file=… line=…`,
> `[Telescope] definitions gather failed:`, `[Telescope] open definition failed:`,
> `[NeoVisual] goto failed: {finder}: {msg}`; (8) **`GotoDispatcher` lives in
> `Telescope/Controller/GotoDispatcher.cs`** (namespace `Telescope.Controller`, `internal`) —
> Telescope.Tests references only Telescope.csproj, so the seam must be a Telescope type for
> its tests to be hermetic (Telescope +9 / NeoVisual +0-edited-only).

### Phase 1 — Commands + dispatcher + Definition finder (BP-1 … BP-9)

- **BP-1** — the `CommandList` IDs (0x0101-0x0103) for the three commands.
- **BP-2** — NEW `MyExtensionPackage.vsct` (the full file content in the artifact).
- **BP-3** — the `.csproj` `VSCTCompile` item + `[ProvideMenuResource("Menus.ctmenu", 1)]`.
- **BP-4** — the pure `GotoDispatcher.Decide(int)` → `DirectJump | OpenOverlay` (1 → direct;
  0 and >1 → overlay) — **`Telescope/Controller/GotoDispatcher.cs`**, namespace
  `Telescope.Controller`, `internal` (a Telescope type so the Telescope.Tests tests are
  hermetic).
- **BP-5** — `DefinitionHit` (the hit model; extends `FileLocation`; **+`Kind`** — the 4-arg
  ctor `(filePath, lineNumber, symbolName, kind)`, matching the `ImplementationHit` precedent).
- **BP-6** — `DefinitionFinder` (Telescope, `Name="Definition"`) — the host-injected gather
  seam (hermetic-testable); the display `{Kind} {SymbolName} — {file}:{line}`; the
  **finder-side** `OrderBy(FilePath).ThenBy(LineNumber)` inside `GatherHits`.
- **BP-7** — `RoslynGatherers.GatherDefinitions` + the pure `MapDefinitionHits`
  (`DeclaringSyntaxReferences` → in-source locations → (path,line) dedupe ONLY — the ordering
  is finder-side per BP-6).
- **BP-8** — the command actions (`ExecuteGoto`/`AddGotoCommand`): resolve the caret symbol →
  gather → `Decide` → direct-jump (`HitOpener`) or `TelescopeLauncher.Open(finder)`; the finder
  registration in `MyExtensionPackage`; `using Telescope.Controller;` for the dispatcher.
- **BP-9** — `FinderNames["telescope-definition"] = "Definition"` (auto-derives the registry
  entry) + the registry-count test 16 → 17 (re-read the post-Gap-3 state first).

### Phase 2 — Unit tests (BP-B1 … BP-B4)

- **BP-B1** — 6 `Run_DefinitionFinder_*` hermetic tests (display/payload/open/preview-jump/
  determinism/gather-summary), inserted after `Run_ImplementationFinder_LineNumberDrivesPreviewJump`
  (re-locate by name). RED: CS0246.
- **BP-B2** — 3 `Run_GotoDispatcher_*` tests (Decide(1)→DirectJump; 0/-1/2/5→OpenOverlay).
  RED: CS0246.
- **BP-B3** — EDIT `Run_ActionsRegistry_ContainsAllBuiltins` (NeoVisual): 16 → 17 +
  `"telescope-definition"` — GUARDED: re-read the file first; proceed only at count 16
  (STOP at 12 = Gap 3 not landed).
- **BP-B4** — the gate: both suites 0-failed; `dotnet build` 0 errors; counts vs `<ACTUAL>`.
  Pinned deltas: Telescope **+9** (delta-only — 172→181, or 190→199 if the columns plan lands
  first); NeoVisual **+0 new, 1 edited** (stays 187).

### Phase 3 — Harness + docs + e2e (BP-C1 … BP-C13)

- **BP-C1** — seed the `GotoProbe` partial-class pair (the multi-hit Definition fixture) +
  the perturbation audit.
- **BP-C2..C5** — register `telescope-goto` (inserted after `telescope-implementation`) with
  4 parts executed via `dte-command.ps1` DTE command execution: goto-definition 1-hit →
  direct (`Shared`@Reader.cs:5 → Models/Shared.cs:1); goto-definition 2-hit → the Definition
  overlay (`open finder=Definition candidates=2`); goto-references ≥2-hit → the References
  overlay; goto-implementation 1-hit → direct (`IShape` → Shape.cs:2). Asserts the pinned
  `goto-direct`/`definitions gathered`/`open finder=` lines + the existing `goto line=`
  fallback + `Wait-ActiveDocumentMatch`.
- **BP-C6..C9** — the docs: spec §3 (the commands + the VsVim mapping note), §4/§5/§8 (the
  literals + counts); AGENTS.md (the counts, the scenario list, the diagnostics list, the
  feature + keybindings bullets); SKILL.md (the wiring + counts).
- **BP-C10** — progress.md: the Gap-6 DONE form at GREEN (the goto CORE ships; any remaining
  Gap 6 scope stays queued).
- **BP-C11/C12** — the e2e-queue rows (workspace E2E-GOTO-1/2 + the canonical
  `docs/e2e-queue.md` section) — with e2e ENABLED these drain at VERIFY.
- **BP-C13** — the lint/self-check phase gate.
- **BP-E2E (the lane)** — `e2e-test-builder` creates `telescope-goto` + proves RED; 
  `verification-agent` executes it + the full suite at VERIFY.

## Verification Trace

| failing test / gate (RED before the change) | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_GotoDispatcher_SingleHitIsDirectJump` / `_ZeroHitsOpensOverlay` / `_MultipleHitsOpenOverlay` (3 new) | BP-4, BP-B2 | RED CS0246 → GREEN: 1→DirectJump; 0/>1→OpenOverlay |
| `Run_DefinitionFinder_*` (6 new — the canonical set incl. `_OpenLogsOpenedDefinition`/`_NameIsDefinition`) | BP-5/6/7, BP-B1 | RED CS0246 → GREEN: display/payload/open/preview-jump/determinism/gather-summary |
| `Run_ActionsRegistry_ContainsAllBuiltins` (edit) | BP-9, BP-B3 | `Expected [17] but got [16]` → GREEN 17 keys incl. `telescope-definition` |
| `Run_ActionsRegistry_TelescopeMapsToFinder` (must stay GREEN) | BP-9 | the registry's telescope keys ≡ FinderNames keys |
| unit gate | BP-B4 | both suites 0-failed; `dotnet build` 0 errors |
| e2e `telescope-goto` (RED then GREEN) | BP-C2..C5, BP-E2E | RED before the source lands; GREEN: the 4 parts' `goto-direct`/`open finder=`/`goto line=` lines + the active-document matches |
| e2e full suite | BP-E2E | all pre-existing scenarios GREEN |
| lints + sweeps | BP-C13 | check-doc-refs 0 unresolved; check-doc-content PASS; `-List` = 40 (39 + telescope-goto) |

**Known-RED allowlist: NONE.** Expected RED = the new unit tests (CS0246) + the new e2e
scenario before the source lands. The verification-agent must NOT flag the NeoVisual count
test's sequencing (it edits only at the post-Gap-3 state) or the double-gather (accepted).

## Hub handoff steps (DEFERRED until Gap 3 GREEN in docs/progress.md)

1. Verify Gap 3's GREEN entry (the precondition — its code/docs have landed; the Done entry
   was pending at aggregation time).
2. Write the assembled plan to `docs/implementation_plan.md` (AFTER the columns plan's
   handoff — this plan is SECOND in the queue).
3. `docs/progress.md`: the goto plan as the next pending item (the e2e-ENABLED lane); the
   Done entry at GREEN; the Decisions entry recording the commands+VsVim-mapping wiring
   decision.
4. The e2e queues: E2E-GOTO-1..2 appended; drained at this plan's VERIFY.
