# Plan — Gap 11: git leader bindings (`g,d` diff, `g,b` blame, `g,h` history)

> **Lane: feature (e2e ENABLED).** No new diagnostic literal (the `command:` path reuses the
> existing `[NeoVisual] leader-binding executed: {seq}` + `Command '...' failed: {msg}` lines)
> — no M-M7 trigger, but the full feature-lane loop applies (e2e RED + VERIFY).
>
> **Source:** the queue's Gap 11 (triage EXTEND/REUSE native 2026-10-03) + the user's
> 2026-10-04 decisions: *"leader+g(represents git)+d for diff b for blame h history … i only
> need git blame, git history and diff anything else will be done in the git overlay … for
> now just implement the blame, history, diff."*
>
> **User decisions recorded:** branches binding DROPPED (`g,b` rebinds branches→blame; the
> user doesn't need in-editor branches — the deferred lazygit overlay covers them); the
> **lazygit-style overlay is DEFERRED** ("one of the last things we do… bonus feature when the
> core of the extension is finished") — `g,g` stays `View.GitChanges` and `g,c` stays
> `Team.Git.Commit` until that overlay ships (the deferred item owns those rebinds).
>
> **Ground truth:** the columns+preview plan is IN FLIGHT (first pending item); the goto plan
> is SECOND. This plan is THIRD — it touches ONLY `default-keybindings.json` + tests + docs +
> the harness (no overlay files — zero conflict with the in-flight work). Baselines move under
> the in-flight plans; all counts are `<ACTUAL>` re-reads at execution time.
>
> **Research (feature-researcher, verified):** `Team.Git.CompareWithUnmodified` = diff the
> active file vs HEAD (HIGH, 3 independent sources); `Team.Git.Annotate` = blame
> (MEDIUM-HIGH); `Team.Git.ViewHistory` = file history (MEDIUM-HIGH); `Team.Git.GoToGitCommits`
> = the commits/history view (AUTHORITATIVE — MS Learn). The `Git.*` names are UNVERIFIED
> candidates — the e2e loop can verify them live later. All require an open Git repo.
> **Recon:** ZERO collisions — no harness/unit fixture types any `g,` sequence; the binding
> path (the Ordinal leader dict + the case-based matcher) is unchanged post-Gap-1; leader
> sequences never collide with tool-window action keys (different routing paths).

## Goal

Three git leader bindings under the existing `g` prefix: `g,d` → diff the active file,
`g,b` → blame the active file (the branches binding is dropped), `g,h` → the active file's
history. Pure `command:` bindings — zero C# changes.

## Approach

**D1 — The binding table (the ONLY source change).** In
`MyExtension/Resources/default-keybindings.json`:
- ADD `"g,d": "command:Team.Git.CompareWithUnmodified"`,
  `"g,b": "command:Team.Git.Annotate"`,
  `"g,h": "command:Team.Git.ViewHistory"`.
- REMOVE `"g,b": "command:Team.Git.Branches"` (replaced by blame — the user's decision).
- UNCHANGED: `"g,g": "command:View.GitChanges"`, `"g,c": "command:Team.Git.Commit"`
  (they rebind to the deferred lazygit overlay when it ships — noted, not this plan).
- The 4 `Ctrl+*` simple shortcuts and all other leader bindings UNTOUCHED. Binding count:
  the current total +3 −1 (an `<ACTUAL>` re-read; post-columns-plan the JSON is untouched by
  it, so the delta is exactly +2 net).

**D2 — The e2e scratch repo (the harness seed).** The `Team.Git.*` commands require an open
Git repo; the scratch solution (`%TEMP%\telescope_scratch`) is not one. The harness's
`Reset-ScratchSolution` gains a seed step: `git init` + an initial commit (git is available —
the repo itself is one; the harness runs `git` for the seed-leak SHA-256 baseline? no — that
is PowerShell hashing; the step adds a `git init`+`commit` invocation). Deterministic, offline
(`git init` needs no network). The planner pins the exact step + the idempotence (a re-seed
deletes the dir first — the existing pattern).

**D3 — Tests.** `tests/NeoVisual.Tests` (the keybinding tests live there):
`Run_Keybinding_DefaultFileHasGitBindings` (NEW, RED): `LoadDefaults()` contains the three
new pairs; does NOT contain `g,b`→`Team.Git.Branches` (the dropped binding — assert the VALUE
is `command:Team.Git.Annotate`); `g,g`/`g,c` unchanged. The existing
`Run_Keybinding_DefaultFileHasTelescopeAndNav`/`...WindowManagement`/`...DiagnosticNav` tests
stay GREEN (additive). Suite delta: +1 (an `<ACTUAL>` re-read).

**D4 — e2e (ENABLED).** A new scenario `neovisual-git-bindings`: fire `Space g d` / `g b` /
`g h` with an editor focused; assert `leader-binding executed: g,d` (etc.) + the ABSENCE of
`Command 'Team.Git.*' failed` lines (the commands exist; the repo is seeded per D2). Created +
proven RED before the build; executed at VERIFY.

**D5 — Docs.** spec.md §3 (the bindings list), AGENTS.md (the keybindings bullet + the
scenario list/count), SKILL.md (the binding list), progress.md (the Gap 11 item → DONE at
GREEN). `<ACTUAL>` re-reads everywhere.

## Acceptance criteria

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | `Space g d` diffs the active file | `leader-binding executed: g,d` + NO `Command 'Team.Git.CompareWithUnmodified' failed` | unit `Run_Keybinding_DefaultFileHasGitBindings`; e2e `neovisual-git-bindings` |
| AC2 | `Space g b` blames the active file (branches is gone) | `leader-binding executed: g,b` + no failure line | same |
| AC3 | `Space g h` opens the active file's history | `leader-binding executed: g,h` + no failure line | same |
| AC4 | `g,g`/`g,c` are unregressed (the deferred overlay owns their rebinds) | existing lines | unit (the unchanged assertions); e2e full re-run |
| AC5 | The scratch repo is seeded so the commands have a target | (harness-side) | the e2e scenario's success |

## Files to be touched

- **Modified:** `MyExtension/Resources/default-keybindings.json` (D1),
  `tools/harness/test-e2e.ps1` (D2 the seed + D4 the scenario),
  `tests/NeoVisual.Tests/Program.cs` (D3), `docs/spec.md`, `AGENTS.md`,
  `.opencode/skills/vs-extension-dev/SKILL.md`, `docs/progress.md` (D5).
- **Not touched:** ANY C# source (zero code changes — pure bindings), the overlay (the
  in-flight columns work), `LeaderSequenceMatcher`/`KeyNames`/`KeybindingConfig`.

## Open risks

1. **The command names (medium).** `Team.Git.Annotate`/`ViewHistory`/`CompareWithUnmodified`
   are MEDIUM-HIGH verified (not MS-Learn-authoritative). If a name is wrong,
   `ExecuteVsCommand` logs `Command '...' failed` and the key is swallowed — never a crash;
   the e2e gate catches it and the fix is a one-line JSON edit.
2. **The scratch repo seed (low).** `git init` in the harness — deterministic; the seed-leak
   guard must allowlist the `.git` directory (the planner pins: exclude `.git` from the seed
   snapshot OR include it — decide; the seed-leak baseline hashes seeded FILES, and `.git`
   adds many — EXCLUDE `.git` from the seed set).
3. **The in-flight plans (process).** Third in queue — the counts are re-reads.

## Build Plan

> **Aggregated (Stage 3)** from `artifacts/gap11-section.md` — the authoritative full detail
> (the exact JSON edits, the test code, the seed step, the scenario body, the doc edits) lives
> there; the steps below are the contract. **e2e ENABLED** (the scenario is created + proven
> RED before the build, executed at VERIFY).
>
> **Pinned corrections (binding):** the JSON goes 36 → 38 bindings (+3 git, −1 branches);
> the unit test is assertion-RED (the Assert API has NO `NotEqual` — assert the `g,b` VALUE is
> `command:Team.Git.Annotate`); the git seed runs at the END of `Reset-ScratchSolution`
> (`git -C`, a local identity, gpgsign off); `.git` is EXCLUDED from the seed set via one
> regex token in `Get-SeedFiles` (`'[\\/](obj|bin|\.git)[\\/]'` — defense-in-depth;
> `Assert-SeedConsistent` needs no change); no `$script:VkB` exists → `Send-Text 'b'`;
> the e2e insertion is after `neovisual-diagnostic-nav`; the final gate = NeoVisual 191 /
> Telescope 199 / 41 scenarios / the lints / `-SelfCheck`.

- **BP-1** — the JSON: +`g,d`/`g,b`/`g,h`, −the branches line; `g,g`/`g,c` unchanged.
- **BP-2** — the unit test `Run_Keybinding_DefaultFileHasGitBindings` (anchor by text between
  `...DiagnosticNav` and `...IsSimpleShortcut`).
- **BP-3** — the git seed at the end of `Reset-ScratchSolution`.
- **BP-4** — the `.git` exclusion in `Get-SeedFiles`.
- **BP-5** — the e2e scenario `neovisual-git-bindings` + the header line (the RED protocol +
  the inline absence gate).
- **BP-6** — the mid-point gate.
- **BP-7..BP-10** — the docs: spec §3; AGENTS (the list + the count + the Done + the
  stale-status fix); SKILL; progress (at GREEN).
- **BP-11** — the final gate: NeoVisual 191 / Telescope 199 / 41 scenarios / the lints /
  `-SelfCheck`.

## Verification Trace

| failing test / gate (RED before the change) | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_Keybinding_DefaultFileHasGitBindings` (new) | BP-1, BP-2 | RED assertion → GREEN: the three pairs + `g,b`→Annotate + `g,g`/`g,c` unchanged |
| e2e `neovisual-git-bindings` (RED then GREEN) | BP-3, BP-4, BP-5 | RED before the JSON lands; GREEN: the three `leader-binding executed:` lines + NO `Command 'Team.Git.*' failed` |
| the seed-leak guard | BP-3, BP-4 | `.git` excluded; the seed baseline unchanged |
| unit gate | BP-6, BP-11 | NeoVisual 191 / Telescope 199 (staggered) |
| lints + `-SelfCheck` + `-List` | BP-11 | 0 unresolved; PASS; 41 scenarios |

**Known-RED allowlist: NONE.** Expected RED = the new unit test + the new e2e scenario before
the JSON lands. The verifier must NOT flag the live command-name risk (the absence gate is the
arbiter — a wrong name is a one-line JSON fix) or the in-flight plans' count drift (re-reads).
