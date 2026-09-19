# Implementation Plan — Item: Harness seeding hardening (mixed EOL + always-reset)

> **Note on prior item:** Item 1 (F45 — centralize log-prefix constants) is
> **COMPLETE** (`Telescope/DiagnosticLog.cs` exists, `Run_LogPrefixes_Pinned`
> passes, suites 42/21, all rg gates pass, and its 19-scenario e2e subset ran
> green — the only 2 failures were known-backlog assertion bugs, not regressions).
> See the `## Done` section in `docs/progress.md`. This plan file is now for the
> seeding hardening item.

---

**Goal:** Stop the e2e harness from creating scratch-solution files with **mixed
line endings**, which makes VS show the "normalize line endings?" modal when a
seeded file is opened (steals focus → breaks tests), and make seeding **always
reset** the scratch solution so stale edits from a prior run never leak into the
next run.

## Root cause (verified 2026-09-19)

`tools/test-e2e.ps1` bootstrap (lines ~982-1021) seeds `%TEMP%\telescope_scratch`:

```powershell
# line 1012 — the offender:
Set-Content -Path (Join-Path $probeDir 'TodoProbe.cs') -Value "// TODO: fix this issue`nclass TodoProbe { }`n"
```

`Set-Content -Value` (no `-NoNewline`) writes the string and then appends the
**platform** newline (CRLF on Windows), while the embedded `` `n `` are **LF**.
Result on disk (empirically confirmed): `TodoProbe.cs` = `CRLF=1 LF=2 mixed=True`.
When `telescope-issues` opens `TodoProbe.cs`, VS detects inconsistent line endings
and shows the "Do you want to normalize the line ending? (windows: cr lf)" modal,
which **steals keyboard focus** and causes the harness's next `Assert-VsFocused` /
key injection to target the wrong window → scenario fails.

Separately, every seed is gated `if (-not (Test-Path ...))` (lines 987, 1006,
1017), so seeding is **not idempotent-resetting**: if a prior run accidentally
wrote into a seeded file, the next run reuses the stale content instead of
restoring the canonical seed.

## Approach

1. **Extract a pure-filesystem `Reset-ScratchSolution([string]$scratchDir)`** in
   `tools/test-e2e.ps1` that ALWAYS resets `$scratchDir` (delete + recreate) and
   seeds every file with **uniform** line endings:
   - Probe console project via `dotnet new console` (always).
   - The 10 `$extraFiles` single-line `// $rel` files → written with explicit
     uniform CRLF (e.g. `[System.IO.File]::WriteAllText($p, "// $rel`r`n")` or
     `Set-Content -NoNewline` + `` `r`n ``).
   - `TodoProbe.cs` → written with **explicit uniform CRLF** (no `-Value`
     platform-newline mix): `[System.IO.File]::WriteAllText($p, "// TODO: fix this issue`r`nclass TodoProbe { }`r`n")`.
   - `Motions.cs` → **kept as pure LF** (already uniform; do NOT change its EOL —
     `telescope-preview-motions` asserts exact caret positions that depend on the
     seeded line lengths).
   - `dotnet new sln` + `dotnet sln add` (always).
2. **Bootstrap calls `Reset-ScratchSolution $scratch`** (replaces the inline
   block at lines ~982-1021).
3. **Add `Assert-SeedConsistent([string]$scratchDir)`** — a test helper that
   asserts every seeded file has **uniform** line endings (not mixed) and matches
   its canonical seed content (no stale edits). Called at bootstrap after the
   reset AND by the `seed-reset` scenario.
4. **Add scenario `seed-reset`** (via `Register-Scenario`) that proves the
   always-reset + uniform-EOL behavior using a **separate temp dir** (NOT the live
   scratch, so it never disturbs the running VS instance).
5. Update the scenario header comment (lines ~7-32) to list `seed-reset`.

## Acceptance criteria

1. `Assert-SeedConsistent $scratch` passes at bootstrap: no seeded file has mixed
   EOL, and each seeded file is byte-identical to its canonical seed.
2. `Reset-ScratchSolution` is idempotent-resetting: after mutating a seeded file
   (append a marker) and re-calling it, the file is restored to canonical (marker
   gone) and `Assert-SeedConsistent` passes again.
3. `seed-reset` scenario passes (exit 0).
4. `telescope-issues` scenario passes without a focus-steal — **regression pair**:
   under the OLD seed it is RED (the "normalize line endings?" modal steals focus →
   `Assert-OverlayFocused`/key injection misses); under the NEW seed it is GREEN
   (no modal, focus stays on the overlay). Prove both directions.
5. `dotnet build` succeeds (no C# change expected — this is harness-only).
6. Both offline unit suites stay green: `tests/Telescope.Tests` 42/42,
   `tests/NeoVisual.Tests` 21/21 (no C# change).

## E2E test plan

New scenario `seed-reset` in `tools/test-e2e.ps1` via `Register-Scenario`:

- Uses a **temp scratch dir** (e.g. `Join-Path $env:TEMP "scratch_seed_test_$PID"`),
  NOT `$scratch`, so the live VS instance's open solution is never disturbed.
- Step 1: `Reset-ScratchSolution $testDir`; then `Assert-SeedConsistent $testDir` →
  must pass (uniform EOL + canonical content).
- Step 2: mutate a seeded file — append `// STALE-MARKER` to `$testDir\Probe\Beta.cs`.
- Step 3: `Reset-ScratchSolution $testDir` again; then assert `Beta.cs` no longer
  contains `STALE-MARKER` and `Assert-SeedConsistent $testDir` passes.
- Step 4: clean up `$testDir`.
- Fails if: `Reset-ScratchSolution`/`Assert-SeedConsistent` are undefined; any
  seeded file has mixed EOL; a stale edit survives a reset; the canonical content
  drifts.

Re-run `telescope-issues` (the scenario that opens `TodoProbe.cs`) to prove the
focus-steal popup is gone.

**No new C# diagnostics** — this is a harness-only fix; the extension code is
unchanged, so no new `[Telescope]`/`[NeoVisual]` lines are required. The reset +
EOL behavior is asserted by the `seed-reset` scenario and the bootstrap
`Assert-SeedConsistent` self-check (pure PowerShell).

## Offline unit tests

None (no C# change). The test surface is PowerShell: the `seed-reset` e2e scenario
+ the bootstrap `Assert-SeedConsistent` self-check. State this explicitly — no
`tests/Telescope.Tests` or `tests/NeoVisual.Tests` change.

## RED evidence (to be produced by the e2e-test-builder)

- The e2e-test-builder ADDS `Assert-SeedConsistent([string]$scratchDir)` (a test
  helper) AND calls it against the CURRENT bootstrap output. It must exist and
  **THROW** on `TodoProbe.cs` because it is `mixed=True` (CRLF=1 LF=2). **The RED
  is the helper detecting the mixed EOL and throwing** — NOT a "command not found"
  failure. If the helper is present and throws on `mixed=True`, that is a clean,
  behavior-based RED.
- `seed-reset` scenario fails on current code (stale marker survives a reset
  because seeding is gated on `Test-Path`).
- **Failure signaling:** `Assert-SeedConsistent` and the `seed-reset` scenario
  signal failure by **throwing** (or returning `$false`), which the harness runner
  `catch` at line ~1102 converts into `exit 1` — this is the harness-equivalent
  "diagnostic". Do not silently swallow a failure.

## Risk notes

- **Do NOT change `Motions.cs` EOL.** It is pure LF (uniform) today; rewriting it
  to CRLF would shift caret positions and break `telescope-preview-motions`.
- The `seed-reset` scenario must use a **temp dir**, never the live `$scratch` —
  deleting the live scratch while the experimental VS has the solution open would
  break the running instance and re-introduce a focus/loading issue.
- **Reset vs devenv ordering (HARD requirement for the Build Plan):**
  `Reset-ScratchSolution` deletes `$scratch`. The bootstrap currently seeds (lines
  ~982-1021) BEFORE the devenv-kill (line ~1055). **The Build Plan's first BP-n
  must ensure the scratch reset happens AFTER any prior devenv is stopped** (kill
  devenv first, or verify no devenv holds the scratch at reset time) so the delete
  cannot fight a live solution or be blocked by a locked dir. Otherwise AC2
  (idempotent reset) breaks before any scenario runs.
- This is prerequisite work: it must land before F45's e2e subset (BP-23) and any
  other e2e verification can be trusted.

---

# Build Plan — Harness seeding hardening (mixed EOL + always-reset)

**Scope contract (do not second-guess):** this is a **harness-only** item. Zero C#
changes: no file under `MyExtension/` or `Telescope/` is touched, no new
`[Telescope]`/`[NeoVisual]` diagnostics are added, no MEF/DI/keybinding wiring
applies. Consequently the net472 / UI-thread / `CardinalMovment` hard requirements
cannot be violated (nothing C# changes). The only file modified is
`tools/test-e2e.ps1` (plus this document). `Assert-SeedConsistent` (lines 243-300)
and the `seed-reset` scenario (lines 1002-1031), both added by the e2e-test-builder,
are **the contract** — do NOT rewrite them; make the seed conform to them. The
runner `catch` (lines 1193-1198) already converts any `throw` into `exit 1`; all
fail-fast checks below rely on that (never swallow).

Line numbers below refer to the RED snapshot of `tools/test-e2e.ps1` (1211 lines);
the build-agent must anchor on the quoted text, not bare numbers.

---

## BP-1 — Bootstrap ordering: stop any prior devenv BEFORE the scratch reset

> **STEP 0 (do this FIRST, before any edit):** record the no-C#-touched baseline
> (`log/seed-baseline.txt`) exactly as shown in BP-4 step 6. This must happen
> BEFORE any file is touched so a C# modification (however unlikely in this
> harness-only item) is caught. Sequence: STEP 0 baseline → BP-1..BP-3 (edits) →
> BP-4 (build + diff).

- **Files**: `tools/test-e2e.ps1` (bootstrap section only).
- **Change**: Relocate the devenv-kill block so the scratch delete can never fight a
  live solution or a locked dir.
  1. INSERT, immediately before the line
     `Write-Step 'Ensure scratch solution (multiple source files)'` (currently line
     1076):
     ```powershell
     # HARD ORDERING REQUIREMENT: stop ANY prior devenv BEFORE the scratch reset below, so the
     # delete cannot fight a live solution or be blocked by a locked scratch dir.
     Get-Process devenv -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
     Start-Sleep -Seconds 3
     ```
  2. DELETE the now-duplicated kill from the "Fresh main VS" section (currently
     lines 1149-1150: `Get-Process devenv ... Stop-Process -Force ...` and
     `Start-Sleep -Seconds 3`), leaving the comment at line 1148 and
     `Start-Process $devenv ...` (line 1152) intact. By the time `Start-Process`
     runs, the killed processes are long gone (seeding takes seconds in between).
- **Verify-with**:
  - Parse check (reused in every BP step) — run from the repo root:
    ```powershell
    $e = $null; $null = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path 'tools/test-e2e.ps1'), [ref]$null, [ref]$e)
    if ($e.Count -gt 0) { $e | ForEach-Object { Write-Error $_.Message }; exit 1 } else { 'PARSE OK: 0 errors' }
    ```
   - **Ordering check is deferred to BP-3** (the `Reset-ScratchSolution $scratch`
     call does not exist until BP-3, so it must NOT be checked here — a check here
     would be a false RED). The relocated kill is the FIRST
     `Get-Process devenv .*Stop-Process -Force` (today line 1149; `Select-Object
     -First 1` correctly skips the later ones at 1164/1206/1210). The full
     kill-before-reset ordering is proven at BP-3.
- **Fails-if**: the bootstrap throws `file is being used by another process` /
  `Remove-Item` lock error on `%TEMP%\telescope_scratch` (kill is not before the
  reset); a second full run without `-KeepVs` leaves a zombie devenv holding the
  scratch.

## BP-2 — Define `Reset-ScratchSolution([string]$scratchDir)` (always-reset, uniform-EOL seeding)

- **Files**: `tools/test-e2e.ps1`.
- **Change**: Add the function verbatim immediately AFTER `Assert-SeedConsistent`'s
  closing `}` (currently line 300) and BEFORE the `# --- telescope-open ---`
  scenario comment (line 302). It is a pure-filesystem function (no VS, no log
  pane) — the `OverlayKeyHandler`-style extraction for the harness:
  ```powershell
  function Reset-ScratchSolution([string]$scratchDir) {
      # ALWAYS reset: delete + recreate. Seeding is NOT gated on Test-Path, so stale edits from a
      # prior run can never leak into the next run. Every file is written with UNIFORM line
      # endings (explicit CRLF for the extra files + TodoProbe.cs; Motions.cs stays pure LF).
      if (Test-Path $scratchDir) { Remove-Item $scratchDir -Recurse -Force -ErrorAction Stop }
      New-Item -ItemType Directory -Force -Path $scratchDir | Out-Null
      $probeDir = Join-Path $scratchDir 'Probe'

      # Probe console project (ALWAYS; $probeDir is fresh, so dotnet new never hits a non-empty dir).
      dotnet new console -n Probe -o $probeDir 2>&1 | Out-Null

      # The 10 extra files: single line "// <rel>" + explicit uniform CRLF; parent dirs created.
      # This OVERWRITES the dotnet-new-generated Program.cs with the canonical content.
      $extraFiles = @(
          'Program.cs',
          'Alpha.cs',
          'Beta.cs',
          'Gamma.cs',
          'Delta.cs',
          'Epsilon.cs',
          'Service.cs',
          'Models/User.cs',
          'Models/Order.cs',
          'Services/AuthService.cs'
      )
      foreach ($rel in $extraFiles) {
          $p = Join-Path $probeDir $rel
          New-Item -ItemType Directory -Force -Path (Split-Path $p) | Out-Null
          [System.IO.File]::WriteAllText($p, "// $rel`r`n")
      }

      # TODO marker for the code-issues finder: EXPLICIT uniform CRLF. The old
      # Set-Content -Value "...`n..." appended the platform CRLF and produced a MIXED-EOL file
      # (CRLF=1 loneLF=2) that made VS show the "normalize line endings?" modal and steal focus.
      [System.IO.File]::WriteAllText(
          (Join-Path $probeDir 'TodoProbe.cs'),
          "// TODO: fix this issue`r`nclass TodoProbe { }`r`n")

      # Deterministic multi-line file for telescope-preview-motions: KEEP pure LF, NO trailing
      # newline (Set-Content -NoNewline). DO NOT change its EOL — the scenario asserts EXACT
      # caret positions (e.g. 'preview caret=14 line=2') that depend on these seeded line lengths.
      Set-Content -Path (Join-Path $probeDir 'Motions.cs') -NoNewline -Value "class Motions`n{`n    int alpha = 1;`n    string beta = `"gamma`";`n}"

      # Solution + project entry (ALWAYS, not gated on Test-Path).
      dotnet new sln -n TelescopeTest -o $scratchDir --format sln 2>&1 | Out-Null
      dotnet sln (Join-Path $scratchDir 'TelescopeTest.sln') add (Join-Path $probeDir 'Probe.csproj') 2>&1 | Out-Null
  }
  ```
  No change to `Assert-SeedConsistent` (lines 243-300) — its canonical map is
  already byte-compatible with the seed above (`// <rel>\r\n` for the 10 files,
  `// TODO: fix this issue\r\nclass TodoProbe { }\r\n`, Motions.cs pure-LF
  no-trailing-newline). The header comment (line 33) already lists `seed-reset`.
- **Verify-with**:
  - Parse check (BP-1 command) still `PARSE OK: 0 errors`.
  - Pre-flight functional check (fast, no VS) — write a throwaway
    `%TEMP%\seed-check.ps1` containing the VERBATIM bodies of `Assert-SeedConsistent`
    (copy from lines 243-300) and the new `Reset-ScratchSolution` above, plus:
    ```powershell
    $d = Join-Path $env:TEMP ("seed_check_" + $PID)
    Reset-ScratchSolution $d
    # TodoProbe.cs must be CRLF=2 LF=0 (uniform) — this is the mixed-EOL RED turning green.
    $b = [System.IO.File]::ReadAllBytes((Join-Path $d 'Probe\TodoProbe.cs'))
    $crlf = 0; $lone = 0
    for ($i = 0; $i -lt $b.Length; $i++) { if ($b[$i] -eq 0x0A) { if ($i -gt 0 -and $b[$i-1] -eq 0x0D) { $crlf++ } else { $lone++ } } }
    if ($crlf -ne 2 -or $lone -ne 0) { throw "TodoProbe.cs EOL: CRLF=$crlf loneLF=$lone (expected CRLF=2 LF=0)" }
    # Motions.cs must stay pure LF (CRLF=0, loneLF=4, no trailing newline) — preview caret pins.
    $m = [System.IO.File]::ReadAllBytes((Join-Path $d 'Probe\Motions.cs'))
    $mcrlf = 0; $mlone = 0
    for ($i = 0; $i -lt $m.Length; $i++) { if ($m[$i] -eq 0x0A) { if ($i -gt 0 -and $m[$i-1] -eq 0x0D) { $mcrlf++ } else { $mlone++ } } }
    if ($mcrlf -ne 0 -or $mlone -ne 4) { throw "Motions.cs EOL changed: CRLF=$mcrlf loneLF=$mlone (must stay pure LF)" }
    Assert-SeedConsistent $d
    # Stale-edit reset proof: mutate, reset, marker must be GONE.
    [System.IO.File]::AppendAllText((Join-Path $d 'Probe\Beta.cs'), "// STALE-MARKER`r`n")
    Reset-ScratchSolution $d
    if (Select-String -Path (Join-Path $d 'Probe\Beta.cs') -Pattern 'STALE-MARKER' -Quiet) { throw 'STALE-MARKER survived a reset' }
    Assert-SeedConsistent $d
    Remove-Item $d -Recurse -Force
    'SEED CHECKS PASSED'
    ```
    This is a copy-based pre-flight only; the authoritative proof is the bootstrap
    self-check (BP-3) and the `seed-reset` scenario (BP-4). **The pre-flight copies
    the VERBATIM function bodies, so it validates the body logic only — the actual
    file's function definition ORDER (e.g. `Reset-ScratchSolution` defined before
    the bootstrap call, not nested inside a scenario) is proven by the BP-3/BP-4
    parse + run, not by this pre-flight.**
  - Grep guard: no `if (-not (Test-Path ...))` gate remains anywhere in
    `Reset-ScratchSolution`.
- **Fails-if**: `TodoProbe.cs` still `mixed=True` (CRLF=1 loneLF=2 — the BP-2 body
  was not applied or `Set-Content -Value` was kept); `Assert-SeedConsistent` throws
  `seed file has MIXED line endings (CRLF=1 loneLF=2): ...\Probe\TodoProbe.cs`;
  `seeded file missing: ...` (dotnet new failed or the delete/recreate was skipped);
  `Motions.cs EOL changed` (the "keep pure LF" rule was violated);
  `STALE-MARKER survived a reset` (a Test-Path gate was reintroduced); the
  `seed-reset` scenario still throws command-not-found for `Reset-ScratchSolution`.

## BP-3 — Bootstrap calls `Reset-ScratchSolution $scratch` + `Assert-SeedConsistent $scratch`

- **Files**: `tools/test-e2e.ps1` (bootstrap section).
- **Change**: Replace the whole inline seeding block — from
  `$scratch = Join-Path $env:TEMP 'telescope_scratch'` (line 1077) through
  `Write-Pass "solution ready: $slnPath"` (line 1115), i.e. the gated
  `New-Item`/`dotnet new console`/`$extraFiles` loop/`TodoProbe.cs`
  `Set-Content`/`Motions.cs`/gated `dotnet new sln` — with:
  ```powershell
  $scratch = Join-Path $env:TEMP 'telescope_scratch'
  $slnPath = Join-Path $scratch 'TelescopeTest.sln'
  Reset-ScratchSolution $scratch
  # Bootstrap self-check: must pass once the seed is uniform + canonical. Throws -> exit 1.
  Assert-SeedConsistent $scratch
  Write-Pass "solution ready: $slnPath"
  ```
  `$slnPath` must remain defined (it feeds `NEOVISUAL_TEST_SOLUTION` at lines
  1142-1143). `$probeDir`/`$extraFiles` are no longer script-scope (nothing after
  line 1115 uses them; the `neovisual-editor-insert` scenario recomputes its own at
  line 725). Keep `Write-Step 'Ensure scratch solution (multiple source files)'`
  (line 1076) as-is; the BP-1 kill block sits just above it.
- **Verify-with**:
  - Parse check (BP-1 command) still `PARSE OK: 0 errors`.
  - Grep: exactly one `Reset-ScratchSolution $scratch` call and exactly one
    `Assert-SeedConsistent $scratch` call in the bootstrap; the old offender
    `Set-Content -Path (Join-Path $probeDir 'TodoProbe.cs')` no longer exists; no
    `if (-not (Test-Path` gate remains in the bootstrap seeding path.
   - **Ordering check (deferred from BP-1, now that both exist)** — the FIRST
     `Get-Process devenv .*Stop-Process -Force` line must be strictly before the
     first `Reset-ScratchSolution $scratch` call line:
     ```powershell
     $f = 'tools/test-e2e.ps1'
     $kill  = (Select-String $f -Pattern 'Get-Process devenv .*Stop-Process -Force' | Select-Object -First 1).LineNumber
     $reset = (Select-String $f -Pattern 'Reset-ScratchSolution \$scratch' | Select-Object -First 1).LineNumber
     if (-not $kill -or -not $reset -or $kill -ge $reset) { throw "ordering violated: kill=$kill reset=$reset" }
     'ORDER OK: kill before reset'
     ```
   - `pwsh tools/test-e2e.ps1 -List` (side-effect-free, exits at line 1041) prints
     `seed-reset` in the list (26 scenarios).
   - The bootstrap self-check passes: a full `pwsh tools/test-e2e.ps1 -Tests seed-reset`
     run reaches `Write-Pass "solution ready: ..."` without the self-check throwing.
- **Fails-if**: the bootstrap throws `seed file has MIXED line endings ... TodoProbe.cs`
  (BP-2 not applied / old `Set-Content` kept); throws `Reset-ScratchSolution :
  command not found` (BP-2 skipped); throws `seeded file missing: ...` (dotnet new
  failed — note `$ErrorActionPreference='Stop'` does not trap native exit codes, so
  a missing file surfaces here instead); `dotnet new` errors on a non-empty folder
  (the delete+recreate in BP-2 was not honored).

## BP-4 — Acceptance sweep (read-only verification; NO source changes)

- **Files**: none modified. Run the exact gates below from the repo root.
- **Change**: none.
- **Verify-with**:
  1. `dotnet build` → succeeds (proves no accidental C#/project change).
  2. `dotnet run --project tests/Telescope.Tests` → **42 passed**.
  3. `dotnet run --project tests/NeoVisual.Tests` → **21 passed**.
  4. `pwsh tools/test-e2e.ps1 -Tests seed-reset` → **exit 0** (bootstrap self-check
     passed; reset is idempotent; `Write-Pass 'seed-reset: reset is idempotent and
     seed EOL is uniform'` printed).
  5. `pwsh tools/test-e2e.ps1 -Tests telescope-issues` → **exit 0** — the
     focus-steal regression pair: under the OLD mixed-EOL seed VS popped the
     "normalize line endings?" modal and the scenario failed; under the NEW uniform
     seed it must assert `preview file=.*TodoProbe\.cs`, `preview caret=\d+ line=1`
     and `opened issue: .*TodoProbe\.cs line=\d+` without any focus loss.
     **Known-backlog caveat:** progress.md backlog item 3 (Error List noise making
     `results count=1` flaky) is a SEPARATE pending item — if ONLY that assert
     fails, it is not this item's RED.
   6. **No-C#-touched baseline (hash + path set).** The working tree is ALREADY
      dirty from prior uncommitted sprint work, so `git status --porcelain` is NOT a
      reliable signal (and a naive before/after diff would miss a modification to an
      already-dirty C# file). Instead, the build-agent must record a baseline
      BEFORE BP-1 and re-verify after BP-4:
      - **Before BP-1** (record this, e.g. to `log/seed-baseline.txt`):
        ```powershell
        Get-ChildItem MyExtension,Telescope,tests -Recurse -File |
          Where-Object { $_.FullName -notmatch '\\(bin|obj|\.vs)\\' } |
          ForEach-Object { "{0}|{1}" -f $_.FullName.Substring($PWD.Path.Length+1), (Get-FileHash $_.FullName -Algorithm SHA256).Hash } |
          Sort-Object | Set-Content 'log/seed-baseline.txt'
        ```
      - **After BP-4** (re-run and diff):
        ```powershell
        Get-ChildItem MyExtension,Telescope,tests -Recurse -File |
          Where-Object { $_.FullName -notmatch '\\(bin|obj|\.vs)\\' } |
          ForEach-Object { "{0}|{1}" -f $_.FullName.Substring($PWD.Path.Length+1), (Get-FileHash $_.FullName -Algorithm SHA256).Hash } |
          Sort-Object | Set-Content 'log/seed-after.txt'
        if ((Compare-Object (Get-Content 'log/seed-baseline.txt') (Get-Content 'log/seed-after.txt'))) {
          throw 'a MyExtension/Telescope/tests source file was modified by this item'
        } else { 'C# BASELINE UNCHANGED' }
        ```
      This passes in a dirty tree AND catches any agent touch of an already-dirty
      C# file (hash changes) or a new C# file (new path). The `Where-Object` filter
      excludes derived output (`bin\`, `obj\`, `.vs\`) so the baseline is stable
      across the BP-4 `dotnet build`/`dotnet run` rebuilds (which regenerate obj
      content) — and it covers untracked-but-real source files (git's dirty-tree
      state is irrelevant; `Get-ChildItem` sees every file on disk).
- **Fails-if**: any of the above fails from a cause introduced by this item — e.g.
  `telescope-preview-motions` caret assertions break (Motions.cs EOL was changed in
  BP-2); `seed-reset` exits 1 (BP-2/BP-3 defective); `dotnet build` or either unit
  suite fails (a C# file was touched — violates the scope contract); the hash
  baseline diff shows a MyExtension/Telescope/tests change (an already-dirty C# file
  was modified, or a new one added).

---

# Verification Trace

| failing check / scenario | implicated steps | expected diagnostic / outcome |
|---|---|---|
| `Assert-SeedConsistent` throws `seed file has MIXED line endings (CRLF=1 loneLF=2): ...\telescope_scratch\Probe\TodoProbe.cs` | BP-2 (TodoProbe.cs explicit CRLF), BP-3 (bootstrap self-check) | `TodoProbe.cs` byte check → `CRLF=2 LF=0` (uniform); `Assert-SeedConsistent $scratch` passes at bootstrap; the throw never appears in the run log |
| Reset RED: `// STALE-MARKER` appended to `Probe\Beta.cs` persists after re-running the CURRENT gated seed block | BP-2 (always delete+recreate, no `Test-Path` gates), BP-3 (bootstrap calls `Reset-ScratchSolution`) | after `Reset-ScratchSolution`, `Select-String $beta 'STALE-MARKER'` returns nothing; `Assert-SeedConsistent` passes again |
| `seed-reset` scenario: `Reset-ScratchSolution` is undefined → command-not-found | BP-2 (function defined), BP-3 (bootstrap wiring) | `pwsh tools/test-e2e.ps1 -List` lists `seed-reset`; `pwsh tools/test-e2e.ps1 -Tests seed-reset` exits 0 |
| Script parse: `pwsh Parser::ParseFile` (any syntax error from the edits) | BP-1, BP-2, BP-3 | `PARSE OK: 0 errors` |
| Bootstrap lock risk: scratch delete blocked by a live/prior devenv | BP-1 (kill-before-reset ordering) | ordering grep `kill < reset` passes; bootstrap reaches `solution ready: ...` with no `file in use` throw |
| `telescope-issues` focus-steal regression (VS "normalize line endings?" modal on open) | BP-2 (uniform TodoProbe.cs EOL) | scenario exits 0; `preview file=.*TodoProbe\.cs`, `preview caret=\d+ line=1`, `opened issue: .*TodoProbe\.cs line=\d+` all asserted |
| `dotnet build` | none (harness-only) | success — proves no C# touched |
| Offline unit suites | none (harness-only) | `tests/Telescope.Tests` **42 passed**; `tests/NeoVisual.Tests` **21 passed** |
| No-C#-touched scope contract | BP-4 (hash + path-set baseline) | baseline diff (`log/seed-baseline.txt` vs `log/seed-after.txt`) → identical; `C# BASELINE UNCHANGED` |
| `telescope-preview-motions` caret pins (guard: Motions.cs EOL untouched) | BP-2 (keep Motions.cs pure LF) | Motions.cs byte check `CRLF=0 loneLF=4`; scenario continues to assert exact carets (`preview caret=14 line=2`, etc.) |

---

# Execution Log

## Attempt 1 — Harness seeding hardening (mixed EOL + always-reset)

**Verdict: GREEN.**

- **Step 0 / BP-1 / BP-2 / BP-3 / BP-4: all done.**
- **BUILD (build-agent):** pass. Changed only `tools/test-e2e.ps1`:
  - BP-1: inserted devenv-kill + `Start-Sleep 3` before the scratch reset (`kill=1126`), deleted the duplicate kill from the "Fresh main VS" section.
  - BP-2: added `Reset-ScratchSolution([string]$scratchDir)` verbatim after `Assert-SeedConsistent`'s `}` (no `Test-Path` gate); pre-flight `SEED CHECKS PASSED`.
  - BP-3: bootstrap now calls `Reset-ScratchSolution $scratch` + `Assert-SeedConsistent $scratch`; ordering `kill=1126 < reset=1131`; `-List` shows 26 scenarios incl. `seed-reset`.
  - BP-4: `dotnet build` ok; Telescope 42/42; NeoVisual 21/21; `C# BASELINE UNCHANGED`.
- **VERIFY (verification-agent):** PASS.
  - `pwsh tools/test-e2e.ps1 -Tests seed-reset,telescope-issues` → exit 0 (both ok).
  - `seed-reset`: `PASS: seed-reset: reset is idempotent and seed EOL is uniform` (STALE-MARKER removed; temp dir, never the live scratch).
  - `telescope-issues`: `open finder=Issues candidates=1`, `preview file=...TodoProbe.cs chars=46`, `preview caret=0 line=1`, `opened issue: ...TodoProbe.cs line=1` — no focus-steal modal. `results count=1` asserted fine (known-backlog flake did not manifest).
  - On-disk EOL: `TodoProbe.cs` = `CRLF=2 loneLF=0` (was CRLF=1 loneLF=2 mixed); `Motions.cs` = `CRLF=0 loneLF=4` (pure LF preserved).
  - Unit: Telescope=pass (42/42), NeoVisual=pass (21/21).
- **Iterations:** 1. No debug-agent needed (no build/unit/e2e failure).
- **Outcome:** the "normalize line endings?" focus-steal popup is gone (TodoProbe.cs is now uniform CRLF); seeding always resets the scratch solution (no stale edits leak). F45's e2e subset (BP-23) is unblocked — re-run next.
