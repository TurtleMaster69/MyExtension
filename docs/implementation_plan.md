# Plan — Gap 11: git leader bindings (`g,d` diff, `g,b` blame, `g,h` history)

> **HANDOFF (2026-10-04, neovim_hub):** gate-APPROVED in the planning-hub session (G1:
> APPROVE; the handoff USER APPROVED) — plan verbatim from
> `.opencode/workspaces/neovim-planning-hub/sessions/neovim-planning-hub-20261004-143017/plans/plan-gap11.md`.
> Count corrections (the plan's `<ACTUAL>` re-read convention): the CURRENT baselines are
> Telescope.Tests **224** (unchanged by this item — its test is in NeoVisual.Tests),
> NeoVisual.Tests **190 → 191** (+1), e2e **41 → 42** (the new `neovisual-git-bindings`).
> The plan's "Telescope 199" footer count is stale-era; the final gate re-reads actuals.
> The in-flight plans this plan's ground-truth note refers to are ALL GREEN (columns
> `492c6c9`, preview-buffer `ced9006`) — zero conflict.

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
  `tools/harness/test-e2e.ps1` (D2 the seed + D4 the scenario; RE-PLAN 2026-10-04: the scenario
  body revised — BP-5), `tools/harness/dte-command.ps1` (RE-PLAN 2026-10-04: the `File.Open`
  mode — BP-5 Change 0),
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

> **RE-PLAN (2026-10-04, after the VERIFY final gate FAILED — fail-twice, fresh-boot run 172 +
> retry run 173):** `neovisual-git-bindings` REGRESSION (allowlist NONE). All three
> `[NeoVisual] leader-binding executed: g,d|g,b|g,h` lines FIRE in both runs and the unit test is
> GREEN (BP-1..BP-4 verified: 38 bindings, the three git pairs, no `Team.Git.Branches`) — but the
> absence gate caught `23:36:05.925 [NeoVisual] Command 'Team.Git.CompareWithUnmodified' failed:
> Command "Team.Git.CompareWithUnmodified" is not available.` in BOTH runs;
> `Team.Git.ViewHistory` (g,h) failed run 172 only (Git-provider warm-up timing — available in
> run 173); `Team.Git.Annotate` (g,b) never failed. BP-3/BP-4 NOT implicated (the seed works —
> `Team.Git.Annotate` was available, proving VS detects the seeded repo; `seed-leak`/`seed-reset`
> PASS — the `.git` exclusion holds). Root cause: "is not available" is DTE's QueryStatus refusal
> (`ExecuteVsCommand` → `dte.ExecuteCommand` throw) — the command NAMES resolve; the availability
> CONTEXT refuses. Verified semantics: `Team.Git.CompareWithUnmodified` diffs the SAVED
> working-directory file vs HEAD (`git diff HEAD` semantics — Sara Ford's blog: "a diff of the
> file at HEAD and the file in the working directory"; the MS kexugit blog: Solution Explorer
> right-click → "compares the latest commit and working directory", no editor buffer involved).
> The seed commits everything and the seed-leak guard guarantees no seeded file is modified
> during the run → the active file is always clean → nothing to diff → the command is disabled.
> The plan's Open risk #1 anticipated a wrong NAME (a one-line JSON fix); the realized risk is
> the availability CONTEXT. **DECISION — option (a), the dirty-file setup:** the scenario
> appends a marker line to the ACTIVE seeded file ON DISK before `g,d` (a disk write IS a saved
> modification — the working tree differs from HEAD → the command is available) and RESTORES the
> exact original bytes in a `finally` (byte-identical → the seed-leak SHA-256 matches the
> bootstrap expected copy → NO allowlist entry, NO `Update-SeedExpected` call;
> `Assert-SeedConsistent` runs at BOOT only — test-e2e.ps1:2468, before the scenario loop — so a
> within-run modify+restore is invisible to it; `seed-reset` reseeds a TEMP dir, not the main
> scratch). Option (b) (a different diff command) REJECTED — the user asked for
> diff-the-working-changes specifically; option (c) (a leak-guard allowlist entry) REJECTED —
> unnecessary once the restore is byte-exact. Plus a BOUNDED warm-up retry for the `g,h` race
> (one re-fire per failed sequence after a 2s grace; the absence gate re-scans the POST-retry
> window only, so a wrong command NAME still fails the scenario). **ZERO C# changes — the
> bindings are correct; only the scenario's setup context was wrong.** BP-1..BP-4 and
> BP-6..BP-10 are LANDED and unchanged; BP-5 (the scenario + a `dte-command.ps1` `File.Open`
> mode) and BP-11 (the gate totals) are revised below.

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
> Telescope 199 / 41 scenarios / the lints / `-SelfCheck`. **(RE-PLAN 2026-10-04: the final-gate
> totals are REVISED — see BP-11: Telescope 224 / 42 scenarios; the scenario body is REVISED —
> see BP-5.)**

- **BP-1** — the JSON: +`g,d`/`g,b`/`g,h`, −the branches line; `g,g`/`g,c` unchanged.
- **BP-2** — the unit test `Run_Keybinding_DefaultFileHasGitBindings` (anchor by text between
  `...DiagnosticNav` and `...IsSimpleShortcut`).
- **BP-3** — the git seed at the end of `Reset-ScratchSolution`.
- **BP-4** — the `.git` exclusion in `Get-SeedFiles`.
- **BP-5** — REVISED (RE-PLAN 2026-10-04) — the e2e scenario `neovisual-git-bindings` + the
  `dte-command.ps1` `File.Open` mode. The header-list line (test-e2e.ps1:32) is LANDED and
  unchanged; the scenario block (test-e2e.ps1:902-955) is REPLACED in place; `dte-command.ps1`
  gains a third custom mode. **ZERO C# changes.**
  - **Files:** `tools/harness/test-e2e.ps1`, `tools/harness/dte-command.ps1`.
  - **Change 0 — `dte-command.ps1` `File.Open` mode (NEW):** the pre-`g,d` setup and every
    retry re-fire must re-activate the seeded file's tab after a git view (diff/annotate/
    history) steals the active document. `ExecuteCommand('File.Open*', path)` is UNTRUSTED in
    this VS build (the helper's own `Solution.Open` note observed `File.OpenProject` ignoring
    its arg and opening the file-picker dialog), so add a DTE-API mode mirroring the existing
    `Solution.Open`/`GetActiveDocument` pattern — insert as the FIRST branch of the
    `Solution.Open` chain (before `elseif ($Arg)`), and extend the trailing `Executed` guard to
    `if ($Command -ne 'Solution.Open' -and $Command -ne 'File.Open')`:

    ```powershell
    # 'File.Open' opens a file via dte.ItemOperations.OpenFile — no file-picker dialog. The
    # ExecuteCommand('File.Open*', path) arg path is UNTRUSTED in this VS build (see the
    # Solution.Open note above: File.OpenProject ignored its arg and opened the dialog). Used by
    # neovisual-git-bindings to re-activate the seeded file's tab after a git view (diff/annotate/
    # history) stole the active document; OpenFile on an already-open file ACTIVATES its tab.
    if ($Command -eq 'File.Open' -and $Arg) {
        Invoke-DteWithTimeout { param($dte, $arg) $dte.ItemOperations.OpenFile($arg) | Out-Null } $TimeoutSec "File.Open '$Arg'" @($dte, $Arg) | Out-Null
        Write-Output "Opened file '$Arg' on PID $DevenvPid"
    }
    ```

  - **Change 1 — the scenario body (REPLACE test-e2e.ps1:902-955).** ORDER pinned: `g,b` →
    `g,h` → re-activate + `File.SaveAll` → dirty ON DISK → `g,d` LAST (a SUCCESSFUL
    `CompareWithUnmodified` opens the diff view and STEALS the active document — a later
    Team.Git command would be refused against the diff buffer; `g,b` does NOT steal it — run
    173 proved `g,h` succeeds right after). Exact replacement body:

    ```powershell
    # --- neovisual-git-bindings ------------------------------------------------
    # Gap 11 git leader bindings (the `g` prefix): Space g d diffs the active file vs HEAD
    # (Team.Git.CompareWithUnmodified), Space g b blames it (Team.Git.Annotate — the old branches
    # binding is DROPPED), Space g h opens its history (Team.Git.ViewHistory). Pure command:
    # bindings — the contract is the EXISTING [NeoVisual] leader-binding executed: diagnostic (no
    # new log literal) PLUS the ABSENCE of the `Command 'Team.Git.*' failed` failure literal (a
    # wrong command name logs it via InputHandler.ExecuteVsCommand and swallows the key — the
    # leader-binding lines alone would not catch it). The scratch repo is seeded by
    # Reset-ScratchSolution (git init + an initial commit), so the commands have a target.
    #
    # RE-PLAN 2026-10-04 (VERIFY runs 172/173, fail-twice): on the fully-clean seed
    # Team.Git.CompareWithUnmodified is REFUSED by DTE QueryStatus ("Command ... is not
    # available") — it diffs the SAVED working-directory file vs HEAD (git diff HEAD semantics)
    # and the seed commits everything, so there was nothing to diff. The scenario therefore
    # DIRTIES the active seeded file ON DISK before g,d and RESTORES the exact original bytes in
    # a finally (byte-identical -> the seed-leak SHA-256 matches the bootstrap expected copy —
    # no allowlist, no Update-SeedExpected). The buffer is clean (File.SaveAll first), so VS
    # 2022's default "Auto-load changes, if saved" reloads it silently — no modal, no focus
    # steal; the marker uses the file's OWN EOL style so the reload never sees mixed EOLs.
    #
    # ORDER: g b and g h fire FIRST on the clean file (both proven available, runs 172/173); g d
    # fires LAST because a SUCCESSFUL CompareWithUnmodified opens the diff view and STEALS the
    # active document (a later Team.Git command would be refused against the diff buffer). The
    # seeded file's tab is re-activated (dte-command.ps1 File.Open -> ItemOperations.OpenFile)
    # before g d and before every retry re-fire.
    #
    # WARM-UP RETRY (bounded): the first Team.Git.* QueryStatus on a fresh boot can land before
    # the Git provider finishes initializing (ViewHistory was refused in run 172, available in
    # 173). After the first pass, any `Command 'Team.Git.*' failed` line triggers ONE retry: a
    # 2s grace, then each failed sequence is re-fired ONCE (logged — a second leader-binding
    # executed: line), then the absence gate re-scans the POST-RETRY window only. A warm-up race
    # (fails once, succeeds on retry) passes; a WRONG COMMAND NAME fails again and throws — the
    # gate's contract holds. The commands' VISUAL effects (diff/blame/history surfaces) are
    # deliberately never asserted (VS-native, not deterministic) — the same discipline as
    # neovisual-window-management's tab-group geometry.
    Register-Scenario 'neovisual-git-bindings' {
        param($vs, $logPath)
        Reset-LogBaseline $logPath
        Enter-NormalContext $vs
        Assert-VsFocused $vs 'git leader bindings'
        $dteCmd = Join-Path $PSScriptRoot 'dte-command.ps1'
        $scratch = Join-Path $env:TEMP 'telescope_scratch'

        # The diff/blame/history target is the ACTIVE document (Get-ActiveDocumentPath is the
        # harness's existing DTE-query wrapper, test-e2e.ps1:253 — it executes no VS command). It
        # must be a seeded scratch file: the dirty/restore below must never touch anything
        # outside the seed tree.
        $targetPath = Get-ActiveDocumentPath $vs.Id
        if (-not $targetPath -or -not $targetPath.StartsWith($scratch, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "neovisual-git-bindings: active document is not a seeded scratch file ('$targetPath') - setup drift"
        }

        # Snapshot BEFORE the first key: the absence gate scans only lines appended after it.
        $preGit = Get-LogCacheIndex $logPath
        $preRetry = $preGit      # final-gate window start (overwritten when the retry runs)

        # 1. Space g b -> blame the active file (clean file — proven available, runs 172/173).
        #    No $script:VkB constant exists (harness-common.ps1 is out of scope); Send-Text 'b'
        #    TapVk's 0x42 unshifted via its letter path — the identical key the matcher sees.
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150    # leader
        Send-Tap $script:VkG; Start-Sleep -Milliseconds 150        # g (prefix)
        Send-Text 'b'; Start-Sleep -Milliseconds 800               # b -> Team.Git.Annotate
        Assert-NewLogLine $logPath "$($script:PfxNeo)leader-binding executed: g,b" 'Space g b fired the blame binding'
        Assert-VsFocused $vs 'git bindings (after g,b)'

        # 2. Space g h -> the active file's history (clean file; the warm-up race is handled by
        #    the bounded retry below).
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150    # leader
        Send-Tap $script:VkG; Start-Sleep -Milliseconds 150        # g (prefix)
        Send-Tap $script:VkH; Start-Sleep -Milliseconds 800        # h -> Team.Git.ViewHistory
        Assert-NewLogLine $logPath "$($script:PfxNeo)leader-binding executed: g,h" 'Space g h fired the history binding'

        # 3. Space g d -> diff the active file vs HEAD. Re-activate the seeded file's tab first
        #    (the annotate/history surfaces above may hold focus), make every buffer clean (a
        #    DIRTY buffer would turn the disk write below into a reload PROMPT — a focus-stealing
        #    modal), then dirty the file ON DISK so the working tree differs from HEAD and the
        #    command is available. Restored byte-exactly in the finally.
        $origBytes = [System.IO.File]::ReadAllBytes($targetPath)
        try {
            & $dteCmd -DevenvPid $vs.Id -Command 'File.Open' -Arg $targetPath 2>$null | Out-Null
            Start-Sleep -Milliseconds 400
            & $dteCmd -DevenvPid $vs.Id -Command 'File.SaveAll' 2>$null | Out-Null
            Start-Sleep -Milliseconds 400
            $hasCrlf = $false
            for ($i = 0; $i -lt $origBytes.Length - 1; $i++) {
                if ($origBytes[$i] -eq 0x0D -and $origBytes[$i + 1] -eq 0x0A) { $hasCrlf = $true; break }
            }
            $eol = if ($hasCrlf) { "`r`n" } else { "`n" }
            [System.IO.File]::AppendAllText($targetPath, "$eol// git-diff probe$eol", [System.Text.Encoding]::ASCII)
            Start-Sleep -Milliseconds 600    # let the silent auto-reload settle before the keys

            Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150    # leader
            Send-Tap $script:VkG; Start-Sleep -Milliseconds 150        # g (prefix)
            Send-Tap $script:VkD; Start-Sleep -Milliseconds 800        # d -> Team.Git.CompareWithUnmodified
            Assert-NewLogLine $logPath "$($script:PfxNeo)leader-binding executed: g,d" 'Space g d fired the diff binding'
            Assert-VsFocused $vs 'git bindings (after g,d)'

            # Settle, then scan the first-pass window for Team.Git.* failures.
            Start-Sleep -Milliseconds 1200
            Update-LogCache $logPath
            $failed = @()
            for ($i = $preGit; $i -lt $script:LogCache.Count; $i++) {
                if ($script:LogCache[$i] -match "Command '(Team\.Git\.[^']*)' failed") { $failed += $Matches[1] }
            }
            $failed = @($failed | Select-Object -Unique)

            if ($failed.Count -gt 0) {
                # ONE bounded retry (the Git-provider warm-up race): 2s grace, re-activate the
                # seeded file's tab (a git view stole it), re-fire each failed sequence ONCE
                # (Send-Text's letter path = the same unshifted VK the matcher sees), then the
                # absence gate re-scans the POST-RETRY window only ($preRetry) — a warm-up race
                # passes, a wrong command name fails again and throws.
                Write-Info "git-bindings: $($failed.Count) Team.Git command(s) failed - warm-up grace, then ONE retry"
                Start-Sleep -Seconds 2
                $preRetry = Get-LogCacheIndex $logPath
                $seqKey = @{ 'Team.Git.CompareWithUnmodified' = 'd'; 'Team.Git.Annotate' = 'b'; 'Team.Git.ViewHistory' = 'h' }
                foreach ($cmd in $failed) {
                    $letter = $seqKey[$cmd]
                    if (-not $letter) { throw "a Team.Git command failed and has no retry sequence: $cmd" }
                    & $dteCmd -DevenvPid $vs.Id -Command 'File.Open' -Arg $targetPath 2>$null | Out-Null
                    Start-Sleep -Milliseconds 400
                    Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150    # leader
                    Send-Tap $script:VkG; Start-Sleep -Milliseconds 150        # g (prefix)
                    Send-Text $letter; Start-Sleep -Milliseconds 800           # re-fire ONCE (bounded)
                }
            }
        }
        finally {
            # Restore the EXACT original bytes: the seed-leak guard (Assert-NoSeedLeak) hashes
            # seeded files at run end against the bootstrap expected copy (Write-SeedExpected,
            # bootstrap line 2472) — a byte-identical restore hashes identical, so NO allowlist
            # entry and NO Update-SeedExpected call are needed. The self-check makes a restore
            # failure surface HERE (named) instead of as a confusing seed-leak failure at run end.
            [System.IO.File]::WriteAllBytes($targetPath, $origBytes)
            Start-Sleep -Milliseconds 600    # let the silent auto-reload settle
            $restored = [System.IO.File]::ReadAllBytes($targetPath)
            if (-not [System.Linq.Enumerable]::SequenceEqual([byte[]]$restored, [byte[]]$origBytes)) {
                throw "neovisual-git-bindings: seed restore FAILED for $targetPath (seed-leak would flag it)"
            }
        }

        # Absence gate (AC1-AC3), POST-RETRY window: no Team.Git.* command may have FAILED after
        # the retry. Settle first so a late failure line lands, then scan (the Assert-NoEnterStorm
        # idiom: Update-LogCache + a direct $script:LogCache scan — never a positive wait, which
        # could never prove absence). A first-pass warm-up failure whose retry succeeded is
        # tolerated (its line predates $preRetry); a persistent failure (a wrong command name, or
        # the dirty mechanism broke) throws here — the gate's contract holds.
        Start-Sleep -Milliseconds 1200
        Update-LogCache $logPath
        for ($i = $preRetry; $i -lt $script:LogCache.Count; $i++) {
            if ($script:LogCache[$i] -match "Command 'Team\.Git\..*' failed") {
                throw "a Team.Git command failed: $($script:LogCache[$i])"
            }
        }
    }
    ```

  - **Mechanism notes (verified 2026-10-04):** `Team.Git.CompareWithUnmodified` diffs the SAVED
    working-directory file vs HEAD (`git diff HEAD` semantics — Sara Ford's blog; the MS kexugit
    blog: Solution Explorer right-click → "compares the latest commit and working directory", no
    editor buffer involved) — a DISK write is therefore a saved modification and the editor
    buffer is irrelevant to availability. The buffer is CLEAN at write time (`File.SaveAll`
    first), so VS 2022's default "Auto-load changes, if saved" reloads SILENTLY (no modal, no
    focus steal); the marker detects the file's OWN EOL from the captured bytes so the reload
    never sees mixed line endings. The `finally` restore + the `SequenceEqual` self-check keep
    `Assert-NoSeedLeak` GREEN with NO allowlist and NO `Update-SeedExpected`
    (`Assert-SeedConsistent` runs at BOOT only — test-e2e.ps1:2468; `seed-reset` reseeds a TEMP
    dir, 2129-2149, not the main scratch). The retry is bounded (one re-fire per failed
    sequence) and logged (the second `leader-binding executed:` lines + a `Write-Info` line).
  - **RED evidence (already proven):** runs 172/173 ARE the RED proof for the dirty-file
    mechanism (the absence gate threw on the clean file, fail-twice). The revised scenario's
    GREEN proof is `-Tests neovisual-git-bindings` → exit 0.
  - **Verify-with:**
    - `pwsh tools/harness/test-e2e.ps1 -Tests neovisual-git-bindings` → exit 0; the log contains
      all three `[NeoVisual] leader-binding executed: g,d|g,b|g,h` lines and ZERO
      `Command 'Team.Git.*' failed` lines in the POST-retry window; the restore self-check is
      silent (no throw).
    - `pwsh tools/harness/test-e2e.ps1 -Tests neovisual-git-bindings,seed-leak` → both exit 0
      (the dirty/restore leaves the seed byte-identical — the leak guard proves it).
    - `pwsh tools/harness/test-e2e.ps1 -List` → 42 registered (unchanged).
    - `pwsh tools/harness/dte-command.ps1 -SelfTest` → exit 0 (the helper's own no-VS seam —
      the new mode shares `Invoke-DteWithTimeout`).
  - **Fails-if:** `-List`/parse fails (a PowerShell syntax slip — run `-SelfCheck`); the
    scenario throws `active document is not a seeded scratch file` (setup drift — no active
    document, or a foreign file is active; check what the earlier scenarios left open); it
    throws `a Team.Git command failed: ...` in the POST-retry window (a wrong command name —
    the one-line JSON fix; OR the dirty mechanism broke — check the marker landed: the file's
    git status must be `modified`); it throws `seed restore FAILED` (the `WriteAllBytes`
    restore didn't land or was non-byte-identical — `seed-leak` would also flag it); it throws
    `a Team.Git command failed and has no retry sequence` (an unknown Team.Git command failed —
    a new/renamed binding the retry map doesn't know); the `File.Open` DTE call throws (the
    helper timed out — VS busy; re-run before diagnosing deeper); the scenario passes but a
    LATER scenario breaks (the git views left focus in a tool window — the per-scenario
    `Enter-NormalContext` of every sibling scenario recovers).
- **BP-6** — the mid-point gate.
- **BP-7..BP-10** — the docs: spec §3; AGENTS (the list + the count + the Done + the
  stale-status fix); SKILL; progress (at GREEN).
- **BP-11** — REVISED (RE-PLAN 2026-10-04) — the final gate: NeoVisual **191** / Telescope
  **224** (the verdict's actual — the original 199 was stale; the columns plan's units landed) /
  **42 scenarios** / the lints / `-SelfCheck`. In order:
  1. `dotnet run --project tests/NeoVisual.Tests` → **191 passed, 0 failed** (VERIFIED in runs
     172/173).
  2. `dotnet run --project tests/Telescope.Tests` → **224 passed, 0 failed**.
  3. `pwsh tools/harness/test-e2e.ps1 -Tests neovisual-git-bindings` → exit 0 (the three
     `leader-binding executed: g,*` lines + the absence gate over the POST-retry window + the
     `finally` restore self-check silent).
  4. `pwsh tools/harness/test-e2e.ps1` → the FULL suite, **42 scenarios**, exit 0 (the final
     gate; the known-RED allowlist below applies — `telescope-goto` is FLAKY pass-on-retry).
  5. `pwsh tools/lint/check-doc-refs.ps1` && `pwsh tools/lint/check-doc-content.ps1` → exit 0.
  6. `pwsh tools/harness/test-e2e.ps1 -SelfCheck` → exit 0 (RE-RUN — BP-5 revised the harness
     after BP-6).
  - **Fails-if:** the full suite fails on `neovisual-git-bindings` (see BP-5's revised
    Fails-if); `seed-leak` fails with `modified: <the active seeded file>` (the `finally`
    restore didn't land — BP-5's restore self-check should have thrown FIRST with a clearer
    message); it fails on an UNRELATED scenario → check the known-RED allowlist
    (`telescope-goto` flaky) + the flaky-retry policy before treating it as a Gap 11
    regression; the JSON binding count is not 38 (BP-1's delta drifted — re-read the file).

## Verification Trace

| failing test / gate | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_Keybinding_DefaultFileHasGitBindings` (VERIFIED GREEN, runs 172/173) | BP-1, BP-2 (LANDED) | the three pairs + `g,b`→Annotate + `g,g`/`g,c` unchanged; 38 bindings |
| e2e `neovisual-git-bindings` — the **g,d availability REGRESSION** (run 172 + 173, fail-twice: `Command 'Team.Git.CompareWithUnmodified' failed: ... is not available.`) | **BP-5 REVISED (the dirty/restore mechanism)** | the disk marker makes the working tree differ from HEAD → the command is AVAILABLE → ZERO `Command 'Team.Git.*' failed` lines; `leader-binding executed: g,d` (it already fired — only the AVAILABILITY changes) |
| e2e `neovisual-git-bindings` — the **g,h warm-up race** (run 172: `Team.Git.ViewHistory` failed; run 173: available) | **BP-5 REVISED (the bounded retry)** | a first-pass failure → 2s grace → ONE re-fire (a second `leader-binding executed: g,h` line + the `Write-Info` retry line) → the POST-retry window has NO failure line; a wrong command NAME fails again → the gate throws (contract holds) |
| e2e `seed-leak` — the dirty/restore must not leak | **BP-5 REVISED (the `finally` restore + the `SequenceEqual` self-check)** | the restored file's SHA-256 equals the bootstrap expected copy → `seed-leak: no seeded file was modified during the run`; the restore self-check throws FIRST if the bytes differ |
| e2e `neovisual-git-bindings` — the re-activation seam | **BP-5 REVISED (`dte-command.ps1` `File.Open`)** | `File.Open` (`ItemOperations.OpenFile`) activates the seeded file's tab after a git view stole it — the retry re-fires against the FILE, not the diff/annotate/history surface |
| e2e `neovisual-git-bindings` — the target guard | **BP-5 REVISED (`Get-ActiveDocumentPath`)** | throws `active document is not a seeded scratch file` on setup drift instead of dirtying a foreign file |
| the seed-leak guard (`.git` exclusion; VERIFIED runs 172/173) | BP-3, BP-4 (LANDED) | `.git` excluded; `seed-leak`/`seed-reset` PASS |
| unit gate | BP-6, BP-11 | NeoVisual 191 / Telescope 224 |
| lints + `-SelfCheck` + `-List` | BP-11 | 0 unresolved; PASS; 42 scenarios |

### Known-RED allowlist (NOT Gap 11 regressions — do not flag)

- **`telescope-goto` — FLAKY (pass-on-retry, count 1; the 2026-10-04 VERIFY runs 172/173).** A
  pre-existing caret/Roslyn race (GREEN in runs 169/170). NOT this item's regression — record
  only; do NOT plan a fix. The harness's flaky-retry policy applies.
- **Known flaky scenarios** — AGENTS.md records "a few scenarios are flaky on retry". An
  UNRELATED scenario failing once in the full-suite gate is retried before being called a
  regression.
- **DROPPED from the allowlist 2026-10-04:** `telescope-results-columns` — GREEN in runs
  172/173 (the columns plan landed); it is a normal GREEN scenario now.
- No OTHER known-RED items exist for this lane. The original "expected RED = the new unit test +
  the new e2e scenario before the JSON lands" is DISCHARGED (both landed GREEN); the live
  command-name risk is also discharged (all three names resolve — the realized risk was the
  availability CONTEXT, fixed scenario-side in BP-5).

## Execution Log

### Attempt 1 — RED + BUILD + VERIFY FAIL (iteration 1) (2026-10-04)

- **RED (e2e-test-builder): RED-CONFIRMED** — the unit test `Run_Keybinding_DefaultFileHasGitBindings`
  (assertion-RED: the pairs missing; 190 passed / 1 failed / 191 total) + the e2e scenario
  `neovisual-git-bindings` (42 registered; ONE VS boot: the `leader-binding executed: g,d` line
  never fires — the binding doesn't exist; the `Keybindings loaded: 36` line proves the
  pre-BP-1 defaults). Right-reason confirmed; no harness breakage.
- **BUILD (build-agent): BP-1/3/4/6/7/8/9 done** — the JSON 36 → 38 (verified ConvertFrom-Json),
  the git seed at the end of `Reset-ScratchSolution`, the `.git` exclusion token, NeoVisual
  **191/0**, both lints PASS, the harness parse OK. No deviations.
- **VERIFY (verification-agent) — FAIL (iteration 1):** 40/42 (run 172 fresh boot + retry 173).
  Units GREEN (Telescope 224/0, NeoVisual 191/0); the harness-health + seed guards all PASS.
  **`neovisual-git-bindings` — REGRESSION (fail-twice):** the three binding lines FIRE, but the
  absence gate caught `Command 'Team.Git.CompareWithUnmodified' failed: ... is not available`
  2/2 — DTE QueryStatus refuses the diff on a CLEAN file (the command diffs the SAVED working
  file vs HEAD; the seed commits everything and the leak guard keeps it clean). `ViewHistory`
  failed run 172 only (the Git-provider warm-up race). **`telescope-goto` — FLAKY x1**
  (pass-on-retry; a pre-existing caret/Roslyn race, GREEN in runs 169/170 — recorded, not a
  regression).
- **Cost:** delegations: 3 | VS boots: 2 | iterations: 1

### Attempt 2 — RE-PLAN + GREEN (2026-10-04)

- **RE-PLAN (implementation-planner):** the fix decided SCENARIO-SIDE (zero C# changes): the
  command's saved-file semantics verified (`git diff HEAD` — Sara Ford / the MS kexugit blog /
  SO); the leak-guard mechanics verified (`Assert-SeedConsistent` at BOOT only; a byte-identical
  restore hashes identical → NO allowlist, NO `Update-SeedExpected`). BP-5 revised: the dirty-
  on-disk + `finally` byte-exact restore (+ the `SequenceEqual` self-check), the ORDER pinned
  `g,b` → `g,h` → dirty → `g,d` LAST (a successful diff steals the active document), the
  bounded warm-up retry (2s grace → ONE re-fire per failed sequence → the POST-retry absence
  window), the NEW `dte-command.ps1` `File.Open` mode (`ItemOperations.OpenFile` — the
  `ExecuteCommand('File.Open*')` path is untrusted). BP-11's totals corrected (Telescope 224 /
  42 scenarios).
- **PLAN REVIEW (4a, re-run): APPROVE** (1 minor — the stale AGENTS.md unit counts, folded into
  the hub's GREEN doc sync; 1 nit — the Change-0 phrasing, functionally identical).
- **BUILD (re-dispatch): the revised BP-5 done** — the scenario block replaced in place
  (test-e2e.ps1:902-1056), the `File.Open` mode as the chain head (the standalone-`if` form
  would fall through into the untrusted `ExecuteCommand` path — the build-agent's correct
  interpretation, ACCEPTED), parse checks 0 errors, the sanity build 0 errors.
  `DEVIATION: BP-5-comment-word -> ACCEPT (comment-only: "see the note below" — the pinned
  placement puts the note below)`.
- **VERIFY (verification-agent) — the FINAL GATE: PASS.** Harness-health first: `-SelfCheck`
  PASS, `-List` 42, both lints PASS. Units staggered: Telescope **224/0**, NeoVisual **191/0**.
  Full e2e FRESH boot (`-TimeoutSec 2400`, run 174): **42/42 GREEN**;
  `neovisual-git-bindings` FIRST-RUN PASS — the three binding lines in the pinned order, the
  first-pass `ViewHistory` warm-up refusal absorbed by the DESIGNED bounded retry (the
  POST-retry window clean), the restore self-check silent, `seed-leak`/`seed-reset` GREEN.
  `telescope-goto` passed first-run (its flaky count stays at the base 1 — no 3rd strike).
  Zero deviations, zero new flakes.
- **Failure-log sweep:** 12 entries read, 0 fixed, 0 queued, 0 annotated (all entries already
  carry FIXED resolutions).
- **Cost:** delegations: 6 | VS boots: 4 | iterations: 1
