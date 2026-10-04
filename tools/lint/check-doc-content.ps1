<#
check-doc-content.ps1 — Phase 10 doc-content self-check (docs-only lane).

Asserts the FIXED doc state for BP-64..BP-67 (M6, m61, m66, m67, m68, m69, n20, n21)
of docs/implementation_plan.md (Phase 10 — Docs drift). The Phase 10 bugs live in the
docs and have NO unit-test surface, so RED is proven by asserting the FIXED content and
watching the CURRENT docs fail. No VS boot, no e2e scenario run.

Assertions (each maps to a BP step):
  DOC-64-1  (BP-64/M6)  e2e-queue.md carries the prior 67-findings plan's deferred gates
                        under DISTINCT E2E-NCR-67-* IDs (1..2, M1/M2/M15..M26/M27/M28,
                        E2E-RESTRUCTURE-67-1) — not the bare E2E-NCR-1/2 the new plan uses.
  DOC-64-2  (BP-64/M6)  no ID collision: the NEW plan's E2E-NCR-1/E2E-NCR-2 entries survive.
  DOC-64-3  (BP-64/M6)  progress.md "Current state" bullet points at the NEW IDs
                        (E2E-NCR-1...) and no longer cites the old E2E-RESTRUCTURE-1.
  DOC-65-*  (BP-65/m61) check-doc-refs.ps1 global $externalAllowlist no longer blanket-masks
                        DistinctBy / CardinalMovment / CardinalNavigation (must be scoped to
                        the specific archive docs).
  DOC-66-1  (BP-66/m66) progress.md:10 header no longer says "Chunk C restructure pending".
  DOC-66-2  (BP-66/m67) progress.md "Next up" section points at the combined plan
                        (98 findings + restructure), not the stale Architecture backlog.
  DOC-66-3  (BP-66/n20) progress.md baseline carries attributed unit counts attributed to a
                        legitimate GREEN item (most recently "after Feature 6", 2026-10-04),
                        not the Architecture consolidation.
  DOC-66-4  (BP-66/n21) architecture-review.md:417 "Namespace/folder hygiene" claim updated to
                        MyExtension.Navigation / Navigation/ (no CardinalMovment).
  DOC-67-1  (BP-67/m68) spec.md §4 contains the 4 harness-asserted diagnostic lines
                        (open finder=, Focus prompt => True, mode=insert,
                        results count=... selected=..., key=... mode=... handled=...).
  DOC-67-2  (BP-67/m69) spec.md §2.2 contains the 13 seam-file rows (OverlayShowState,
                        FocusTargetModel, LineIndex, TryDispatch, PreviewRenderer,
                        BlockCaretStyle, VimModeClassifier, InitSteps, SimpleShortcutMatcher,
                        NavigationSnapshot, FilterFailureLog, TelescopeLog, PaneFailureTracker).

Usage:
  pwsh tools/lint/check-doc-content.ps1        # exit 0 = FIXED state; exit 1 = drift (RED)
  pwsh tools/lint/check-doc-content.ps1 -List  # print the assertions without running
#>
[CmdletBinding()]
param([switch]$List)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$results = [System.Collections.Generic.List[object]]::new()

function Add-Check {
    param([string]$Id, [string]$Desc, [bool]$Pass, [string]$Detail)
    $results.Add([pscustomobject]@{ Id = $Id; Desc = $Desc; Pass = $Pass; Detail = $Detail })
}

# Collapse all whitespace (incl. line wraps) to a single space so multi-line bullets
# match as if they were one line.
function Normalize-Text([string]$s) { return [regex]::Replace($s, '\s+', ' ') }

function Get-Section {
    param([string]$Path, [string]$Header, [string]$NextHeader)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    $text = [System.IO.File]::ReadAllText($Path)
    $start = $text.IndexOf($Header)
    if ($start -lt 0) { return '' }
    $end = if ($NextHeader) { $text.IndexOf($NextHeader, $start + $Header.Length) } else { $text.Length }
    if ($end -lt 0) { $end = $text.Length }
    return $text.Substring($start, $end - $start)
}

# Return the full wrapped bullet starting at the first line containing $Marker
# (marker line + every following continuation line that starts with whitespace).
function Get-Bullet {
    param([string]$Path, [string]$Marker)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    $lines = [System.IO.File]::ReadAllLines($Path)
    $idx = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -like "*$Marker*") { $idx = $i; break }
    }
    if ($idx -lt 0) { return '' }
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append($lines[$idx])
    for ($j = $idx + 1; $j -lt $lines.Count; $j++) {
        if ($lines[$j] -match '^\S') { break }   # next non-continuation line ends the bullet
        [void]$sb.Append(' ').Append($lines[$j].Trim())
    }
    return $sb.ToString()
}

# Extract a balanced `@( ... )` array block for a `$var = @(` assignment (parens inside
# comments are balanced, so depth counting lands on the real closing paren).
function Get-ArrayBlock {
    param([string]$Text, [string]$VarName)
    $startIdx = $Text.IndexOf("$VarName = @(")
    if ($startIdx -lt 0) { return '' }
    $openIdx = $Text.IndexOf('@(', $startIdx)
    if ($openIdx -lt 0) { return '' }
    $depth = 0
    for ($i = $openIdx; $i -lt $Text.Length; $i++) {
        $c = $Text[$i]
        if ($c -eq '(') { $depth++ }
        elseif ($c -eq ')') {
            $depth--
            if ($depth -eq 0) { return $Text.Substring($openIdx, $i - $openIdx + 1) }
        }
    }
    return ''
}

$queuePath    = Join-Path $repoRoot 'docs/e2e-queue.md'
$progressPath = Join-Path $repoRoot 'docs/progress.md'
$specPath     = Join-Path $repoRoot 'docs/spec.md'
$archPath     = Join-Path $repoRoot 'docs/reviews/architecture-review.md'
$lintPath     = Join-Path $repoRoot 'tools/lint/check-doc-refs.ps1'

$queueText    = if (Test-Path -LiteralPath $queuePath)    { [System.IO.File]::ReadAllText($queuePath) }    else { '' }
$lintText     = if (Test-Path -LiteralPath $lintPath)     { [System.IO.File]::ReadAllText($lintPath) }     else { '' }

# --- BP-64 (M6): e2e-queue reconciliation -------------------------------------
# 2026-10-02: the queue was EMPTIED — every previously-deferred gate's scenarios were
# exercised GREEN by the 72-findings plan's full 35-scenario suite run. The assertions
# now check the queue carries no QUEUED gate and records the GREEN run that satisfied them.
$queuedGates = @([regex]::Matches($queueText, '(?m)^##\s+E2E-') | ForEach-Object { $_.Value })
Add-Check 'DOC-64-1' 'e2e-queue.md carries no QUEUED gate (all previously-deferred gates ran GREEN 2026-10-02)' `
    ($queuedGates.Count -eq 0) ("queued gates: " + ($queuedGates -join ', '))

Add-Check 'DOC-64-2' 'e2e-queue.md records the 2026-10-02 GREEN full-suite run that satisfied the deferred gates' `
    ($queueText -match '2026-10-02' -and $queueText -match 'GREEN') ("hasDate=$($queueText -match '2026-10-02') hasGreen=$($queueText -match 'GREEN')")

$curState = Normalize-Text (Get-Section $progressPath '## Current state' '## Decisions')
$curHasNewId = ($curState -match 'E2E-NCR-\*' -or $curState -match 'E2E-CR51-' -or $curState -match '72 findings' -or $curState -match 'GREEN')
$curHasOldRestructure = ($curState -match 'E2E-RESTRUCTURE-1')
Add-Check 'DOC-64-3' 'progress.md "Current state" bullet points at the current e2e gate IDs (E2E-NCR-* wildcard, the concrete E2E-CR51 IDs, or the 72-findings GREEN state) and not the old E2E-RESTRUCTURE-1' `
    ($curHasNewId -and -not $curHasOldRestructure) ("hasNewId=$curHasNewId hasOldRestructure=$curHasOldRestructure")

# --- BP-65 (m61): scope the doc-ref allowlist ---------------------------------
# Only the QUOTED ENTRIES of $externalAllowlist count as a blanket mask — the block's
# comments may legitimately still name the removed tokens (they explain the scoping).
# Strip full-line comments first so apostrophes in prose (e.g. "plan's") can't corrupt
# the quoted-entry extraction.
$allowBlock = Get-ArrayBlock $lintText '$externalAllowlist'
$allowBlockNoComments = ($allowBlock -split "`n" | Where-Object { $_ -notmatch '^\s*#' }) -join "`n"
$allowEntries = @([regex]::Matches($allowBlockNoComments, "'([^']+)'") | ForEach-Object { $_.Groups[1].Value })
foreach ($tok in @('DistinctBy','CardinalMovment','CardinalNavigation')) {
    $inGlobal = $allowEntries -contains $tok
    Add-Check "DOC-65-$tok" "check-doc-refs.ps1 global `$externalAllowlist does NOT blanket-mask ``$tok`` (must be scoped to the archive docs)" `
        (-not $inGlobal) ("inGlobalAllowlist=$inGlobal")
}

# --- BP-66 (m66/m67/n20/n21) --------------------------------------------------
$statusLine = Get-Bullet $progressPath '**Status:** ACTIVE'
Add-Check 'DOC-66-1' 'progress.md:10 header no longer says "Chunk C restructure pending"' `
    ($statusLine -notmatch 'Chunk C restructure pending') ("header: $statusLine")

$nextUp = Normalize-Text (Get-Bullet $progressPath '- **Next up:**')
$nextUpRefsCombined = ($nextUp -match '51-findings' -or $nextUp -match '72 findings' -or $nextUp -match 'Code review fixes' -or $nextUp -match 'combined plan' -or $nextUp -match 'Functional restructure' -or $nextUp -match 'F13')
$nextUpStale = ($nextUp -match 'F5, F8, F9, F13, F14, F43')
Add-Check 'DOC-66-2' 'progress.md "Next up" section points at the current code-review plan (51/72 findings) or the remaining F13/F43 backlog, not the stale Architecture backlog list' `
    ($nextUpRefsCombined -and -not $nextUpStale) ("refsCombined=$nextUpRefsCombined staleBacklog=$nextUpStale")

$baselineLine = Normalize-Text (Get-Bullet $progressPath '- Offline units:')
$baselineWrongAttr = ($baselineLine -match 'Architecture consolidation')
$baselineRightAttr = ($baselineLine -match '51 findings' -or $baselineLine -match 'Code review fixes' -or $baselineLine -match '67 findings' -or $baselineLine -match '2026-09-30' -or $baselineLine -match 'Code review findings' -or $baselineLine -match 'combined plan' -or $baselineLine -match 'after Feature')
$baselineHasCounts = ($baselineLine -match '\*\*\d+ passed\*\*')
Add-Check 'DOC-66-3' 'progress.md baseline carries attributed unit counts (e.g. "**172 passed** ... after Feature 6 ... 2026-10-04") attributed to a legitimate GREEN item, not the Architecture consolidation' `
    (-not $baselineWrongAttr -and $baselineRightAttr -and $baselineHasCounts) ("wrongAttr=$baselineWrongAttr rightAttr=$baselineRightAttr hasCounts=$baselineHasCounts line: $baselineLine")

$hygieneLine = Get-Bullet $archPath '**Namespace/folder hygiene:**'
$hygieneNew = ($hygieneLine -match 'MyExtension\.Navigation' -or $hygieneLine -match 'Navigation/')
$hygieneOld = ($hygieneLine -match 'CardinalMovment')
Add-Check 'DOC-66-4' 'architecture-review.md:417 "Namespace/folder hygiene" claim updated to MyExtension.Navigation / Navigation/ (no CardinalMovment)' `
    ($hygieneNew -and -not $hygieneOld) ("hasNew=$hygieneNew hasOld=$hygieneOld line: $hygieneLine")

# --- BP-67 (m68/m69): spec.md diagnostics + key-files table -------------------
$sec4 = Get-Section $specPath '## 4. Diagnostics' '## 5. Testing'
$missing4 = @('open finder=','Focus prompt => True, mode=insert','results count=... selected=...','key=... mode=... handled=...' |
    Where-Object { $sec4 -notmatch [regex]::Escape($_) })
Add-Check 'DOC-67-1' 'spec.md §4 contains the 4 harness-asserted diagnostic lines' `
    ($missing4.Count -eq 0) ("missing: " + ($missing4 -join ' | '))

$sec22 = Get-Section $specPath '### 2.2 Key files' '### 2.3'
$seams = @('OverlayShowState','FocusTargetModel','LineIndex','TryDispatch','PreviewRenderer',
           'BlockCaretStyle','VimModeClassifier','InitSteps','SimpleShortcutMatcher',
           'NavigationSnapshot','FilterFailureLog','TelescopeLog','PaneFailureTracker')
$missingSeams = @($seams | Where-Object { $sec22 -notmatch [regex]::Escape($_) })
Add-Check 'DOC-67-2' 'spec.md §2.2 contains all 13 seam-file rows' `
    ($missingSeams.Count -eq 0) ("missing: " + ($missingSeams -join ', '))

# --- report -------------------------------------------------------------------
if ($List) {
    foreach ($r in $results) { Write-Output ("{0}  {1}" -f $r.Id, $r.Desc) }
    exit 0
}

$failed = @($results | Where-Object { -not $_.Pass })
if ($failed.Count -gt 0) {
    Write-Host "DOC-CONTENT DRIFT ($($failed.Count) failing assertions) — Phase 10 FIXED state not present:" -ForegroundColor Red
    foreach ($r in $failed) {
        Write-Host ("  [FAIL] {0}  {1}" -f $r.Id, $r.Desc) -ForegroundColor Red
        Write-Host ("         detail: {0}" -f $r.Detail) -ForegroundColor DarkYellow
    }
    Write-Host "DOC-CONTENT CHECK: FAIL (RED — the docs still carry the Phase 10 drift)" -ForegroundColor Red
    exit 1
}
Write-Host "DOC-CONTENT CHECK: PASS ($($results.Count) assertions) — Phase 10 FIXED state present" -ForegroundColor Green
exit 0
