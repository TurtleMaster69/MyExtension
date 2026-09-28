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
    [int]$TimeoutSec = 180
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
$expHive = 'Exp'

# A real solution to open so the editor actually loads (VsVim + editor infra active). Use the
# classic .sln format — VS's "Open a project or solution" command opens .sln files, not .slnx.
$scratch = Join-Path $env:TEMP 'telescope_scratch'
$slnPath = Join-Path $scratch 'TelescopeTest.sln'

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
function Assert-Budget { if ($stopwatch.Elapsed.TotalSeconds -gt $TimeoutSec) { throw "Timed out after $TimeoutSec s" } }

# ---------------------------------------------------------------------------
# Resolve tool paths
# ---------------------------------------------------------------------------
$vsRoot = Resolve-VsRoot
$msbuild = Join-Path $vsRoot 'MSBuild\Current\Bin\MSBuild.exe'
$devenv  = Join-Path $vsRoot 'Common7\IDE\devenv.exe'
if (-not (Test-Path $msbuild)) { throw "MSBuild not found at $msbuild" }
if (-not (Test-Path $devenv))  { throw "devenv not found at $devenv" }
Write-Info "VS root: $vsRoot"

function Find-VsWindow {
    $p = Get-Process devenv -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($p) { return $p }
    return $null
}

# ---------------------------------------------------------------------------
# 1. Create the scratch solution (once) so the editor context really exists.
# ---------------------------------------------------------------------------
Write-Step "Ensure scratch solution"
if (-not (Test-Path $slnPath)) {
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    if (-not (Test-Path (Join-Path $scratch 'Probe\Probe.csproj'))) {
        dotnet new console -n Probe -o (Join-Path $scratch 'Probe') 2>&1 | Out-Null
    }
    # Classic .sln (not .slnx) so VS's "Open a project or solution" command can open it.
    dotnet new sln -n TelescopeTest -o $scratch --format sln 2>&1 | Out-Null
    dotnet sln $slnPath add (Join-Path $scratch 'Probe\Probe.csproj') 2>&1 | Out-Null
    dotnet build $slnPath -v q 2>&1 | Out-Null
}
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
[Environment]::SetEnvironmentVariable('NEOVISUAL_TEST_SOLUTION', $slnPath, 'User')
$env:NEOVISUAL_TEST_SOLUTION = $slnPath
[Environment]::SetEnvironmentVariable('NEOVISUAL_LOG_DIR', $logDir, 'User')
$env:NEOVISUAL_LOG_DIR = $logDir
$env:NEOVISUAL_LOG_INDEX = $runIndex

# Kill ALL devenv so we always start a fresh main VS that carries the env var into the
# experimental instance it spawns.
Get-Process devenv -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
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
Write-Pass "main VS open (PID $($mainVs.Id))"
Start-Sleep -Seconds 10   # let the solution finish loading before DTE build

# Kill any running experimental instance so the fresh deploy launches cleanly.
Get-Process devenv -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match 'Experimental' } | Stop-Process -Force -ErrorAction SilentlyContinue
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
    if (-not $KeepVs) { Get-Process devenv -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }
    exit 1
}

Write-Pass "insert mode confirmed"
Write-Pass "prompt accepted query '$Query'"
Write-Pass "results rendered (count>0)"
Write-Host ''
Write-Host "RESULT: PASS" -ForegroundColor Green
if (-not $KeepVs) { Get-Process devenv -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }
exit 0
