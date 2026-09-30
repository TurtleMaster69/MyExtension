# iterate-telescope.ps1
#
# Solo end-to-end harness for the Telescope overlay. Builds + deploys the VSIX into the VS
# Experimental Instance via MSBuild (fast, deterministic), launches devenv DIRECTLY with a real
# solution open (no "select project/folder" start window), opens Telescope (leader + F + T),
# types a query, and asserts on the runtime trace at <solution>\log\<index>-neovisual-exp.log.
#
# Exit code: 0 = PASS, 1 = FAIL.
#
# Usage:  pwsh tools/iterate-telescope.ps1 [-KeepVs] [-Query 'pro'] [-TimeoutSec 180]
#
#   -KeepVs     keep the experimental VS instance open after the run (default: kill it)
#   -Query      text to type into the Telescope prompt (default 'pro')
#   -TimeoutSec overall budget before we declare the run failed

param(
    [switch]$KeepVs,
    [string]$Query = 'pro',
    [int]$TimeoutSec = 180,
    [switch]$SelfCheck
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Shared harness helpers (prefix vars, Write-*, key injection, log waits, foreground,
# VS-root resolution) — dot-sourced so this script and the other harness scripts share one
# byte-compatible implementation.
. (Join-Path $PSScriptRoot 'harness-common.ps1')

# Per-run log files live in <solution>\log\, indexed so the main-VS and experimental-VS logs of
# the same run are paired (neovisual-<index>-main.log / neovisual-<index>-exp.log).
$logDir = Join-Path $root 'log'

# Run index: an auto-incrementing integer (1, 2, 3, ...) derived from the highest existing
# `*-neovisual-*.log` in the log dir, so each run gets a fresh, increasing index. The same index is
# shared by the paired main-VS and experimental-VS log files of a run.
$existingLogs = Get-ChildItem $logDir -Filter '*-neovisual-*.log' -ErrorAction SilentlyContinue
$runIndex = 1
foreach ($f in $existingLogs) {
    if ($f.BaseName -match '^(\d+)-neovisual') {
        $n = [int]$Matches[1]
        if ($n -ge $runIndex) { $runIndex = $n + 1 }
    }
}
$logPath = Join-Path $logDir "$runIndex-neovisual-exp.log"
$debugLogPath = Join-Path $logDir "$runIndex-neovisual-main.log"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# A real solution to open so the editor actually loads (VsVim + editor infra active). Use the
# classic .sln format — VS's "Open a project or solution" command opens .sln files, not .slnx.
$scratch = Join-Path $env:TEMP 'telescope_scratch'
$slnPath = Join-Path $scratch 'TelescopeTest.sln'

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
function Assert-Budget { if ($stopwatch.Elapsed.TotalSeconds -gt $TimeoutSec) { throw "Timed out after $TimeoutSec s" } }

# M36: PIDs of the VS instances THIS run spawned (main VS + experimental instance). The harness
# must never kill a devenv it did not spawn — scope every cleanup kill to this set only, so a run
# does not terminate the user's other open VS instances.
$script:SpawnedVsPids = [System.Collections.Generic.List[int]]::new()

function Add-SpawnedVs([object]$proc) {
    if ($proc -and $proc.Id -and -not $script:SpawnedVsPids.Contains($proc.Id)) {
        $script:SpawnedVsPids.Add($proc.Id)
    }
}

function Save-AllDocuments([int]$devenvPid) {
    # Save every open document in the given VS instance BEFORE killing it (leak evidence + clean
    # next-run start). Best-effort: a missing/unresponsive instance is reported but not fatal.
    if (-not $devenvPid) { return }
    $dteCmd = Join-Path $PSScriptRoot 'dte-command.ps1'
    if (-not (Test-Path $dteCmd)) { Write-Info 'dte-command.ps1 missing; cannot SaveAll before stop'; return }
    try {
        & $dteCmd -DevenvPid $devenvPid -Command 'File.SaveAll' 2>$null | Out-Null
        Write-Info "saved all open documents (PID $devenvPid) before stopping VS"
    } catch {
        Write-Info "could not File.SaveAll on PID $devenvPid ($($_.Exception.Message)); stopping VS anyway"
    }
}

function Stop-VsPids([int[]]$pids) {
    # Shared kill: stops ONLY the explicitly-listed PIDs (never a blanket `Get-Process devenv`),
    # saving open documents first. The self-check drives this directly with dummy PIDs.
    foreach ($id in $pids) {
        Save-AllDocuments $id
        try { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue } catch { }
    }
}

function Stop-SpawnedVs {
    # Kill ONLY the VS instances this run spawned (M36). Never a blanket `Get-Process devenv`.
    Stop-VsPids @($script:SpawnedVsPids)
    $script:SpawnedVsPids.Clear()
}

function Stop-HarnessVs {
    # Pre-spawn cleanup (M36): kill ONLY devenv instances that look harness-spawned — a main VS
    # window titled 'MyExtension' or an 'Experimental' window. Never a blanket `Get-Process devenv
    # | Stop-Process`, which would terminate the user's unrelated open VS instances.
    $targets = @(Get-Process devenv -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowTitle -match 'MyExtension' -or $_.MainWindowTitle -match 'Experimental' })
    Stop-VsPids @($targets | ForEach-Object { $_.Id })
}

function Reset-ScratchSolution {
    # m29: ALWAYS reset the scratch solution — delete + recreate. Seeding is NOT gated on
    # Test-Path, so a stale scratch from a prior run can never be reused.
    if (Test-Path $scratch) { Remove-Item $scratch -Recurse -Force -ErrorAction Stop }
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    dotnet new console -n Probe -o (Join-Path $scratch 'Probe') 2>&1 | Out-Null
    # Classic .sln (not .slnx) so VS's "Open a project or solution" command can open it.
    dotnet new sln -n TelescopeTest -o $scratch --format sln 2>&1 | Out-Null
    dotnet sln $slnPath add (Join-Path $scratch 'Probe\Probe.csproj') 2>&1 | Out-Null
    dotnet build $slnPath -v q 2>&1 | Out-Null
}

function Get-SeedSnapshot {
    # Deterministic snapshot of the seeded scratch (relative path + content hash), excluding build
    # output (obj/bin) — used by -SelfCheck to prove a second seeding run is deterministic. The .sln
    # embeds a randomly-generated project GUID per `dotnet new sln` run, so GUIDs are normalized
    # before hashing: the check compares structure + canonical content, not the random GUID.
    Get-ChildItem -Path $scratch -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
        Sort-Object FullName |
        ForEach-Object {
            $rel = $_.FullName.Substring((Resolve-Path $scratch).Path.Length + 1)
            $content = [System.IO.File]::ReadAllText($_.FullName)
            $content = [regex]::Replace($content, '\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}', '{GUID}')
            $sha = [System.Security.Cryptography.SHA256]::Create()
            try {
                $hash = [BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($content))).Replace('-', '')
            } finally { $sha.Dispose() }
            "{0}|{1}" -f $rel, $hash
        }
}

if ($SelfCheck) {
    # No-VS seam (M36/m29): prove the PID-scoped kill, the Process-scope env, and the always-reset
    # seeding work WITHOUT booting VS. This is the ONLY allowed iterate-telescope.ps1 invocation
    # that does not boot VS.
    try {
        Write-Step 'SelfCheck (no VS)'

        # (1) kill scope: spawn two dummy processes, add ONE PID, Stop-SpawnedVs must kill exactly
        # the listed one and leave the other alive.
        $dummy1 = Start-Process pwsh -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 30' -PassThru
        $dummy2 = Start-Process pwsh -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 30' -PassThru
        try {
            $script:SpawnedVsPids.Add($dummy1.Id)
            Stop-SpawnedVs
            $dummy1.Refresh()
            $dummy2.Refresh()
            if (-not $dummy1.HasExited) { throw 'SelfCheck: Stop-SpawnedVs did not kill the listed PID' }
            if ($dummy2.HasExited) { throw 'SelfCheck: Stop-SpawnedVs killed an unlisted PID (blanket kill leaked)' }
            Write-Pass 'SelfCheck: Stop-SpawnedVs killed exactly the listed PID'
        } finally {
            if (-not $dummy2.HasExited) { Stop-Process -Id $dummy2.Id -Force -ErrorAction SilentlyContinue }
        }

        # (2) env scope: NEOVISUAL_TEST_SOLUTION must never be written to User scope — assert it is
        # unchanged before/after the SelfCheck run and is not the scratch solution path (a stale
        # empty-string value from a pre-fix run is tolerated; a real write of $slnPath is not).
        $envBefore = [Environment]::GetEnvironmentVariable('NEOVISUAL_TEST_SOLUTION', 'User')
        $envAfter = [Environment]::GetEnvironmentVariable('NEOVISUAL_TEST_SOLUTION', 'User')
        if ($envBefore -ne $envAfter) { throw 'SelfCheck: NEOVISUAL_TEST_SOLUTION User-scope changed' }
        if ($envAfter -eq $slnPath) { throw 'SelfCheck: NEOVISUAL_TEST_SOLUTION was written to User scope' }
        Write-Pass 'SelfCheck: NEOVISUAL_TEST_SOLUTION User-scope unchanged (not written by the harness)'

        # (3) seed determinism (m29): run the always-reset seeding twice; the second run must be
        # deterministic (same file set + same canonical content).
        Reset-ScratchSolution
        $run1 = @(Get-SeedSnapshot)
        Reset-ScratchSolution
        $run2 = @(Get-SeedSnapshot)
        if (($run1 -join "`n") -ne ($run2 -join "`n")) { throw 'SelfCheck: second seeding run was not deterministic' }
        Write-Pass 'SelfCheck: seeding is deterministic across runs'

        Write-Host 'SELFTEST PASS' -ForegroundColor Green
        exit 0
    } catch {
        Write-Fail "SELFTEST FAIL: $($_.Exception.Message)"
        exit 1
    }
}

# ---------------------------------------------------------------------------
# Resolve tool paths
# ---------------------------------------------------------------------------
$vsRoot = Resolve-VsRoot
$msbuild = Join-Path $vsRoot 'MSBuild\Current\Bin\MSBuild.exe'
$devenv  = Join-Path $vsRoot 'Common7\IDE\devenv.exe'
if (-not (Test-Path $msbuild)) { throw "MSBuild not found at $msbuild" }
if (-not (Test-Path $devenv))  { throw "devenv not found at $devenv" }
Write-Info "VS root: $vsRoot"

# ---------------------------------------------------------------------------
# 1. Create the scratch solution (once) so the editor context really exists.
# ---------------------------------------------------------------------------
Write-Step "Ensure scratch solution"
Reset-ScratchSolution
Write-Pass "solution ready: $slnPath"
Assert-Budget

# ---------------------------------------------------------------------------
# 2. Deploy the latest build via Visual Studio's own Run (F5) config. VS's project system
#    builds, deploys the VSIX into the experimental hive, and launches the experimental
#    instance — guaranteed to always install/update the latest code. We drive it via DTE on the
#    main VS (which must have the MyExtension solution open). Poll for completion, no fixed waits.
# ---------------------------------------------------------------------------
Write-Step "Deploy via VS Run (DTE Debug.Start) + launch experimental instance"

# Ensure the main VS is running with the solution open. The log DIR is set in both the registry
# (so the user's own future F5 runs keep writing into the same log folder) and the current process
# environment (so the devenv we spawn inherits it). The run INDEX is set ONLY in the current
# process environment — the extension computes the next incrementing index itself when it doesn't
# inherit a valid one, so manual F5 runs never reuse a stale/frozen index.
# M36: NEOVISUAL_TEST_SOLUTION is PROCESS-scope only — the spawned devenv inherits it via the
# process environment, and it never persists past this run (no User-scope write). NEOVISUAL_LOG_DIR
# is intentionally User-scoped (documented opt-in): the user's own F5 runs keep writing to the same
# log folder. The run INDEX is set ONLY in the current process environment — the extension computes
# the next incrementing index itself when it doesn't inherit a valid one, so manual F5 runs never
# reuse a stale/frozen index.
$env:NEOVISUAL_TEST_SOLUTION = $slnPath
[Environment]::SetEnvironmentVariable('NEOVISUAL_LOG_DIR', $logDir, 'User')
$env:NEOVISUAL_LOG_DIR = $logDir
$env:NEOVISUAL_LOG_INDEX = $runIndex

# Kill harness-spawned devenv (title-scoped: 'MyExtension'/'Experimental') so we always start a
# fresh main VS that carries the env var into the experimental instance it spawns. Never a blanket
# `Get-Process devenv | Stop-Process` — that would terminate the user's unrelated VS instances.
Stop-HarnessVs
Start-Sleep -Seconds 3

Write-Info "opening main VS with the solution..."
Start-Process $devenv -ArgumentList "`"$(Join-Path $root 'MyExtension.slnx')`"" -ErrorAction Stop
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$mainVs = $null
while ($sw.Elapsed.TotalSeconds -lt 60) {
    $mainVs = Get-Process devenv -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match 'MyExtension' } | Select-Object -First 1
    if ($mainVs) { break }
    Start-Sleep -Milliseconds 500
}
if (-not $mainVs) { Write-Fail 'Main VS did not open the solution'; exit 1 }
Add-SpawnedVs $mainVs
Write-Pass "main VS open (PID $($mainVs.Id))"
Start-Sleep -Seconds 10   # let the solution finish loading before DTE build

# Kill any running experimental instance so the fresh deploy launches cleanly (title-scoped).
Stop-HarnessVs
Start-Sleep -Seconds 2

# Clear the trace oracle for this run (fresh log) before Debug.Start launches the exp instance.
if (Test-Path $logPath) { Remove-Item $logPath -Force }
$logDir = Split-Path $logPath
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }

# Launch the experimental instance via VS's Run (Debug.Start). VS's project system builds the
# solution, deploys the VSIX into the experimental hive, and launches the experimental instance
# — its config guarantees the latest code is installed. Poll for the exp instance, not a wait.
& (Join-Path $PSScriptRoot 'dte-command.ps1') -DevenvPid $mainVs.Id -Command 'Debug.Start' | Out-Null
Write-Pass "Debug.Start issued"

$vsProc = $null
$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt 90) {
    $vsProc = Get-Process devenv -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match 'Experimental' -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($vsProc) { break }
    Start-Sleep -Milliseconds 400
}
if (-not $vsProc) { Write-Fail 'Experimental VS instance did not appear'; exit 1 }
Add-SpawnedVs $vsProc
Write-Pass "Experimental instance found (PID $($vsProc.Id))"
Assert-Budget

# Poll for the extension's keyboard hook to be installed (real signal, not a fixed wait).
Write-Info "waiting for keyboard hook..."
if (-not (Wait-LogContains $logPath "$($script:PfxHook)installed" 90000)) {
    Write-Fail 'keyboard hook was not installed (extension may not have loaded)'
    if (Test-Path $logPath) { Get-Content $logPath -Tail 20 | ForEach-Object { Write-Info $_ } }
    exit 1
}
Write-Pass "keyboard hook installed"
Assert-Budget

# The extension itself auto-opens the solution (NEOVISUAL_TEST_SOLUTION). Wait for that to
# happen so a real editor context exists before we open Telescope.
Write-Info "waiting for extension to auto-open the solution..."
if (-not (Wait-LogContains $logPath "$($script:PfxMyExt)auto-opened solution" 30000)) {
    Write-Fail 'extension did not auto-open the solution'
    if (Test-Path $logPath) { Get-Content $logPath -Tail 20 | ForEach-Object { Write-Info $_ } }
    exit 1
}
Write-Pass "extension auto-opened solution ($slnPath)"
Assert-Budget

# ---------------------------------------------------------------------------
# 4. Bring VS to foreground, open Telescope: leader (Space) then F then T.
# ---------------------------------------------------------------------------
Write-Step "Open Telescope (Space -> F -> T)"

$telOpen = $false
for ($attempt = 1; $attempt -le 3 -and -not $telOpen; $attempt++) {
    Write-Info "attempt $attempt/3"
    Bring-ToForeground $vsProc.MainWindowHandle
    # Escape first to guarantee a non-typing context, then the leader sequence.
    [KbInject]::TapVk(0x1B)
    Start-Sleep -Milliseconds 400
    [KbInject]::TapVk(0x20)   # Space (leader)
    Start-Sleep -Milliseconds 150
    [KbInject]::TapVk(0x46)   # F
    Start-Sleep -Milliseconds 150
    [KbInject]::TapVk(0x54)   # T
    Assert-Budget

    # Overlay open is confirmed by the log line "[Telescope] open finder=Files".
    $telOpen = Wait-LogContains $logPath "$($script:PfxTel)open finder=Files" 15000
    if (-not $telOpen) { Start-Sleep -Seconds 1 }
}
if (-not $telOpen) {
    Write-Fail 'Telescope overlay did not open'
    if (Test-Path $logPath) { Get-Content $logPath -Tail 30 | ForEach-Object { Write-Info $_ } }
    exit 1
}
Write-Pass "Telescope overlay opened"
Assert-Budget

# ---------------------------------------------------------------------------
# 5. Type the query as virtual-key presses (VsVim/editor process KeyDown; the prompt is a
#    TextBox so it accepts these directly).
# ---------------------------------------------------------------------------
Start-Sleep -Milliseconds 1200   # allow focus + initial render
$vsProc.Refresh()
Bring-ToForeground $vsProc.MainWindowHandle
Start-Sleep -Milliseconds 300
Send-Text $Query
Start-Sleep -Milliseconds 1200   # allow fzf to run

# ---------------------------------------------------------------------------
# 6. Assert on the trace oracle.
# ---------------------------------------------------------------------------
Write-Step "Assert on $logPath"
if (-not (Test-Path $logPath)) { Write-Fail "neovisual.log missing"; exit 1 }
if (-not (Test-Path $debugLogPath)) { Write-Fail "debug log missing ($debugLogPath)"; exit 1 }
Write-Pass "both per-run logs exist (index $runIndex):"
Write-Info "  exp  = $logPath"
Write-Info "  main = $debugLogPath"
$log = Get-Content $logPath
Write-Info "--- tail of neovisual.log ---"
$log | Select-Object -Last 25 | ForEach-Object { Write-Info $_ }
Write-Info ("------------------------------")

$failures = New-Object System.Collections.Generic.List[string]

# Insert mode is the initial mode; the log's Focus line reports mode=insert.
if (-not ($log -match 'mode=insert')) {
    $failures.Add('never saw "mode=insert" in log (prompt may not be in insert mode)')
}
$querySeen = $false
foreach ($line in $log) {
    if ($line -match "promptChanged query='$([regex]::Escape($Query))'") { $querySeen = $true; break }
}
if (-not $querySeen) {
    $failures.Add("prompt never contained query '$Query' (typing did not reach the prompt)")
}
$resultsOk = $false
foreach ($line in $log) {
    if ($line -match 'results count=(\d+)' -and [int]$Matches[1] -gt 0) { $resultsOk = $true; break }
}
if (-not $resultsOk) {
    $failures.Add('no result with count>0 found in log')
}

# ---------------------------------------------------------------------------
# 7. Close overlay (Esc) and report.
# ---------------------------------------------------------------------------
Write-Step "Close overlay (Escape)"
Close-Telescope $vsProc $logPath

if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Fail $f }
    Write-Host ''
    Write-Host "RESULT: FAIL" -ForegroundColor Red
    if (-not $KeepVs) { Stop-SpawnedVs }
    exit 1
}

Write-Pass "insert mode confirmed"
Write-Pass "prompt accepted query '$Query'"
Write-Pass "results rendered (count>0)"
Write-Host ''
Write-Host "RESULT: PASS" -ForegroundColor Green
if (-not $KeepVs) { Stop-SpawnedVs }
exit 0
