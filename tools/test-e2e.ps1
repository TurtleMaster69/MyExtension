# test-e2e.ps1
#
# Live end-to-end test suite. Boots the VS Experimental Instance with the extension deployed and a
# real solution open, then runs every functionality scenario against that LIVE instance — asserting
# on the runtime trace log (the same oracle iterate-telescope.ps1 uses).
#
# Scenarios (all run against one live instance by default; use -Tests to run a subset):
#   telescope-open        Space F T opens the Telescope overlay
#   telescope-search      typing filters candidates (promptChanged + results count)
#   telescope-navigate    normal-mode j/k move selection across many files; i returns to search
#   telescope-wrap        selection wraps around the result list (k at 0 -> last, j at last -> 0)
#   telescope-mode        insert <-> normal mode toggling (Esc/i/a)
#   telescope-open-file   Enter on a match opens the file in the editor
#   telescope-issues      Space F D: warnings/errors/TODO finder filters, previews, opens at line
#   telescope-references  Space F R: lists read/write references to the caret symbol, previews+opens at line
#   telescope-implementation  Space F I: lists implementations of the caret symbol, previews+opens at the decl line
#   telescope-grep      Space F G: grep finder searches files for the query, previews + opens at the hit line
#   telescope-prompt-motions  normal-mode prompt h/l/w/b/e/0/$ caret motions over the query
#   telescope-preview-motions preview pane h/l/j/k/w/b/e/0/$/g/G motions over a seeded file
#   telescope-q-close    q closes the overlay in normal mode
#   telescope-open-file-normal  Enter selects the match in NORMAL mode
#   telescope-open-file-searchbox  type a single-match query, WAIT for the filter to settle, Enter opens it
#   telescope-open-file-navigation  type a multi-match query, j to index 1, Enter opens the moved-to row
#   telescope-no-selection  j/k on an empty result list is a no-op (selection stays 0)
#   neovisual-window-nav  Ctrl+H/J/K/L fire Cardinal navigation (shortcut-binding + navigate)
#   neovisual-leader      Space+W fires a leader binding (leader-binding executed: W)
#   neovisual-toolwindow  Solution Explorer: hjkl navigation + i/Esc input-mode (search box)
#   neovisual-explorer-toggle  Space+E opens, then closes, then reopens Solution Explorer
#   neovisual-explorer-open    l expands the fold, j/k navigate, Enter opens a file
#   neovisual-explorer-open-o  o opens the selected file
#   neovisual-explorer-collapse  h collapses the fold (after l expands it)
#   neovisual-explorer-rename  r starts rename (F2), Escape cancels
#   neovisual-explorer-add     a runs the Add Item command
#   neovisual-explorer-move    m runs the Move command
#   neovisual-explorer-move-editor-focus  editor focused + stale frame: m must NOT fire a tree action
#   explorer-open-navigation   g selects the first source file programmatically (UIHierarchy), o opens it
#   explorer-open-searchbox    i focuses the search box, type a query, o opens the filtered result
#   telescope-preview   preview shows selected file; Ctrl+L/Ctrl+H switch list<->preview; vim motions in preview
#   neovisual-editor-insert  insert-mode typing reaches the editor (hook must not swallow text)
#   neovisual-textinput-motions  Command Window: h/l/w/b/e/a/A/I caret/insert motions + block caret
#   seed-reset    filesystem-only: scratch seeding always resets (stale edits removed) + uniform EOL
#   seed-leak     filesystem-only: NO seeded file was written into during the run (write-leak guard)
#
# Exit code: 0 = all selected scenarios passed, 1 = any failed.
#
# Usage:
#   pwsh tools/test-e2e.ps1                       # all scenarios
#   pwsh tools/test-e2e.ps1 -Tests telescope-open # a single scenario
#   pwsh tools/test-e2e.ps1 -Tests telescope-navigate,telescope-mode
#   pwsh tools/test-e2e.ps1 -Tests telescope-open -KeepVs
#   pwsh tools/test-e2e.ps1 -List                 # list available scenarios
#   pwsh tools/test-e2e.ps1 -Tests telescope-search -NoBootstrap   # reuse an already-booted instance
#
# -KeepVs      keep the spawned VS instances alive on exit (default: kill ONLY the spawned PIDs).
# -NoBootstrap reuse an already-booted Experimental instance (no kill/reseed/main-VS/Debug.Start);
#              requires a prior boot without -NoBootstrap. Use for retries/batches to avoid a reboot.
#
# Side effects (M-N5 — this is NOT a read-only run):
#   - Writes per-run logs to log/<index>-neovisual-{exp,main}.log.
#   - Reseeds the scratch solution at %TEMP%\telescope_scratch (delete + recreate).
#   - Generates a bootstrap expected-result copy of every seeded file (log/seed-expected/) so the
#     seed-leak scenario can prove no seeded file was written into during the run.
#   - Sets NEOVISUAL_TEST_SOLUTION / NEOVISUAL_LOG_DIR / NEOVISUAL_LOG_INDEX in PROCESS scope only
#     (the spawned main VS inherits them; they do NOT persist past this run).
#   - May kill devenv instances this run spawned (or harness-spawned 'MyExtension'/'Experimental'
#     VS during the fresh bootstrap) — never unrelated devenv on the machine.

param(
    [string[]]$Tests = @(),
    [switch]$KeepVs,
    [switch]$List,
    [switch]$NoBootstrap,
    [int]$TimeoutSec = 300
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $root 'log'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Info($msg) { Write-Host "    $msg" }
function Write-Pass($msg) { Write-Host "    PASS: $msg" -ForegroundColor Green }
function Write-Fail($msg) { Write-Host "    FAIL: $msg" -ForegroundColor Red }

# Scenario registry: name -> scriptblock. Each scriptblock receives the VS process object and must
# throw (or return $false) on failure; the runner reports and continues.
$script:Scenarios = [ordered]@{}

# M-M5: PIDs of the VS instances THIS run spawned (main VS + experimental instance). The harness
# must never kill a devenv it did not spawn — scope every cleanup kill to this set only, so a run
# does not terminate the user's other open VS instances.
$script:SpawnedVsPids = [System.Collections.Generic.List[int]]::new()

function Add-SpawnedVs([object]$proc) {
    if ($proc -and $proc.Id -and -not $script:SpawnedVsPids.Contains($proc.Id)) {
        $script:SpawnedVsPids.Add($proc.Id)
    }
}

function Save-AllDocuments([int]$devenvPid) {
    # Save every open document in the given VS instance BEFORE killing it. Two reasons:
    #  1. Leak evidence: a scenario that typed into an open file leaves its content only in the
    #     editor buffer until saved; a force-kill discards it, so the leak can never be inspected.
    #  2. Clean teardown: VS must not be killed with unsaved changes, or the NEXT run greets the
    #     user with the "Visual Studio did not close properly / there are unsaved changes" prompt.
    # Resolves the instance's DTE from the ROT by PID (the same helper the harness already uses to
    # open the scratch solution) and runs File.SaveAll. Best-effort: a missing/unresponsive
    # instance is reported but not fatal (the caller still stops the process).
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

function Stop-SpawnedVs {
    # Kill ONLY the VS instances this run spawned (M-M5). Never a blanket `Get-Process devenv`.
    # Save open documents first (leak evidence + clean next-run start; see Save-AllDocuments).
    foreach ($id in @($script:SpawnedVsPids)) {
        Save-AllDocuments $id
        try { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue } catch { }
    }
    $script:SpawnedVsPids.Clear()
}

function Stop-HarnessVs {
    # Pre-spawn cleanup (M-M5): kill ONLY devenv instances that look harness-spawned — a main VS
    # window titled 'MyExtension' or an 'Experimental' window. Never a blanket `Get-Process devenv
    # | Stop-Process`, which would terminate the user's unrelated open VS instances.
    # Save open documents first so a previous run's leak evidence survives and this run starts from
    # a clean state (no "VS did not close properly / unsaved changes" prompt).
    $targets = @(Get-Process devenv -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowTitle -match 'MyExtension' -or $_.MainWindowTitle -match 'Experimental' })
    foreach ($p in $targets) { Save-AllDocuments $p.Id }
    foreach ($p in $targets) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}

function Write-ToolsHash {
    # M-C1: materialize log/tools-hash.txt on every harness run, so the hub's VERIFY `tools/`-changed
    # flag is always computable against a FRESH hash (and check-doc-refs.ps1 no longer allowlists the
    # file's absence — a genuinely missing hash now fails the lint). SHA-256 of every file under tools/.
    $toolsDir = Join-Path $PSScriptRoot '.'
    $hashPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'log\tools-hash.txt'
    $lines = Get-ChildItem -LiteralPath $toolsDir -Recurse -File |
        ForEach-Object { "{0}|{1}" -f $_.FullName.Substring((Resolve-Path $toolsDir).Path.Length + 1), (Get-FileHash $_.FullName -Algorithm SHA256).Hash } |
        Sort-Object
    New-Item -ItemType Directory -Force -Path (Split-Path $hashPath) | Out-Null
    $lines | Set-Content -LiteralPath $hashPath
}

function Register-Scenario([string]$name, [scriptblock]$body) {
    $script:Scenarios[$name] = $body
}

# ---------------------------------------------------------------------------
# Scenario definitions
# ---------------------------------------------------------------------------

# Helpers used inside scenarios (defined at script scope).
$script:VkEscape = 0x1B
$script:VkSpace = 0x20
$script:VkEnter = 0x0D
$script:VkTab = 0x09

# Per-scenario log baseline: assertions search the whole post-baseline window (never advance), so
# an assert may re-match a line that an earlier helper (e.g. Open-Telescope) already confirmed.
$script:LogBaseline = 0

# Single source for log prefixes (regex-escaped) — keep in sync with
# Telescope/DiagnosticLog.cs; pinned by unit test Run_LogPrefixes_Pinned.
$script:PfxNeo = '\[NeoVisual\] '
$script:PfxTel = '\[Telescope\] '
$script:PfxHook = '\[Hook\] '
$script:PfxMyExt = '\[MyExtension\] '

function Reset-LogBaseline([string]$logPath) {
    $script:LogBaseline = if (Test-Path $logPath) { (Get-Content $logPath).Count } else { 0 }
}

function Send-Tap([int]$vk) { [KbInject]::TapVk([uint16]$vk) }
function Send-Shift([int]$vk) {
    # Shift+<key> chord (e.g. G = move to last) via keybd_event.
    [Win32.Kbd]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero)
    [KbInject]::TapVk([uint16]$vk)
    [Win32.Kbd]::keybd_event(0x10, 0, 2, [UIntPtr]::Zero)  # KEYEVENTF_KEYUP
}
function Send-Text([string]$text) {
    foreach ($ch in $text.ToCharArray()) {
        [KbInject]::TapVk([int][char]::ToUpper($ch))
        Start-Sleep -Milliseconds 60
    }
}
function Send-Ctrl([int]$vk) {
    # Ctrl+<key> chord via keybd_event.
    [Win32.Kbd]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
    [KbInject]::TapVk([uint16]$vk)
    [Win32.Kbd]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)  # KEYEVENTF_KEYUP
}
function Bring-ToForeground([IntPtr]$hwnd) {
    [KbInject]::TapVk(0x12) | Out-Null   # Alt resets the foreground lock
    Start-Sleep -Milliseconds 150
    [Win32.Fg]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 300
}

function Enter-NormalContext([object]$vs) {
    # Ensure the focused editor is NOT in VsVim insert mode (where Space types a literal space
    # instead of acting as the leader key). Multiple Escapes guarantee a non-typing context.
    Bring-ToForeground $vs.MainWindowHandle
    foreach ($i in 1..3) { Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 200 }
    Start-Sleep -Milliseconds 300
}

function Assert-VsFocused([object]$vs, [string]$what) {
    # Verify the foreground window belongs to the experimental VS instance before sending keys,
    # so we know we are giving input to the right window (never to the main VS or another app).
    $h = [Win32.Fg]::GetForegroundWindow()
    if ($h -eq [IntPtr]::Zero) { throw "no foreground window when expecting: $what" }
    $pid2 = 0
    [Win32.Fg]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
    if ([int]$pid2 -ne $vs.Id) {
        throw "foreground window is PID $pid2 but expected VS PID $($vs.Id) when: $what"
    }
}

function Get-ForegroundTitle([object]$vs) {
    # Return the foreground window's TITLE text (or '' when there is no foreground window). The
    # overlay sets Title = "Telescope" (still its window text even with WindowStyle.None), so this
    # distinguishes the overlay HWND from the VS main window HWND (same process id).
    $h = [Win32.Fg]::GetForegroundWindow()
    if ($h -eq [IntPtr]::Zero) { return '' }
    $sb = New-Object System.Text.StringBuilder 256
    [Win32.Fg]::GetWindowText($h, $sb, $sb.Capacity) | Out-Null
    return $sb.ToString()
}

function Wait-OverlayForeground([object]$vs, [int]$maxMs = 5000) {
    # POSITIVE bounded wait for a MATERIALISED state: the OS foreground window must be the overlay
    # itself — same PID as the experimental VS instance AND window text "Telescope". A PID-only
    # check also passes for the VS main window (same process), the exact defect that let injected
    # Enter race OS activation. Waits for the observable foreground-title change, never an absence
    # timer and never a fixed sleep. Returns $true once the state holds, $false on timeout.
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalMilliseconds -lt $maxMs) {
        $h = [Win32.Fg]::GetForegroundWindow()
        if ($h -ne [IntPtr]::Zero) {
            $pid2 = 0
            [Win32.Fg]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
            if ([int]$pid2 -eq $vs.Id -and (Get-ForegroundTitle $vs) -eq 'Telescope') { return $true }
        }
        Start-Sleep -Milliseconds 100
    }
    return $false
}

function Assert-OverlayFocused([object]$vs) {
    # The overlay is modal and owns focus while open, BUT it shares the VS process id, so a
    # PID-only check passes for the VS MAIN window too (the exact defect that let injected Enter
    # race OS activation). Require the foreground HWND to be the ACTUAL overlay: same PID AND
    # window text "Telescope". This is a materialised-state gate (no timer), not an absence check.
    Assert-VsFocused $vs 'Telescope overlay should own focus'
    if (-not (Wait-OverlayForeground $vs 5000)) {
        $title = Get-ForegroundTitle $vs
        throw "foreground window is not the Telescope overlay after 5000 ms (foreground title: '$title', expected 'Telescope')"
    }
}

function Get-ActiveDocumentPath([int]$devenvPid) {
    # Harness-only query through the existing DTE helper (tools/dte-command.ps1): returns the FULL
    # PATH of the active document, or '' when none. VIEW-INDEPENDENT — VS reuses an already-open tab
    # without creating a text view, so this (not `editor-view-opened`) is the right oracle for
    # "the editor now shows the selected file". No product diagnostic is added or changed.
    $dteCmd = Join-Path $PSScriptRoot 'dte-command.ps1'
    if (-not (Test-Path $dteCmd)) { return '' }
    try {
        $out = & $dteCmd -DevenvPid $devenvPid -Command 'GetActiveDocument' 2>$null
        if ($out) { return ([string]($out | Select-Object -Last 1)).Trim() }
    } catch { }
    return ''
}

function Focus-SolutionExplorer([int]$devenvPid) {
    # A successful `o`/Enter moves keyboard focus to the opened document, so a walk loop cannot keep
    # navigating the tree. Re-focus the Solution Explorer tree with the native command the controller
    # itself uses (`View.SolutionExplorer`) so the next l/j reaches the tree. Harness-only.
    $dteCmd = Join-Path $PSScriptRoot 'dte-command.ps1'
    if (-not (Test-Path $dteCmd)) { return }
    try { & $dteCmd -DevenvPid $devenvPid -Command 'View.SolutionExplorer' 2>$null | Out-Null } catch { }
}

function Wait-ActiveDocumentMatch([int]$devenvPid, [string]$pattern, [int]$maxMs = 3000) {
    # POSITIVE bounded wait: returns the active-document path once it matches $pattern, else ''.
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalMilliseconds -lt $maxMs) {
        $now = Get-ActiveDocumentPath $devenvPid
        if ($now -and ($now -match $pattern)) { return $now }
        Start-Sleep -Milliseconds 300
    }
    return ''
}

function Open-Telescope([object]$vs, [string]$logPath) {
    # After a previous overlay close, focus returns to the editor, which VsVim may leave in
    # INSERT mode — where Space types a literal space instead of starting a leader sequence.
    # Hammer Escape a few times first so the editor is in a non-typing (normal) context, then
    # try the leader sequence (Space F T).
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Bring-ToForeground $vs.MainWindowHandle
        foreach ($i in 1..3) { Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 200 }
        Start-Sleep -Milliseconds 300
        Send-Tap $script:VkSpace;  Start-Sleep -Milliseconds 150
        Send-Tap 0x46;             Start-Sleep -Milliseconds 150  # F
        Send-Tap 0x54;                                             # T
        # The overlay is open only once it owns focus (modal + prompt focused). Waiting for BOTH
        # the open line AND the focused prompt guarantees subsequent injected keys land in the
        # overlay, not in the editor underneath.
        if ((Wait-NewLogLine $logPath "$($script:PfxTel)open finder=Files" 15000) -and
            (Wait-NewLogLine $logPath "$($script:PfxTel)Focus prompt => True, mode=insert" 5000)) {
            if (Wait-OverlayForeground $vs 5000) {
                Assert-OverlayFocused $vs
                return
            }
        }
        Start-Sleep -Milliseconds 1000
    }
    throw 'Telescope overlay did not open'
}

function Open-TelescopeIssues([object]$vs, [string]$logPath) {
    # Space F D opens the code-issues finder. Same focus discipline as Open-Telescope: hammer
    # Escape first (get VsVim out of insert), then wait for the overlay to own focus.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Bring-ToForeground $vs.MainWindowHandle
        foreach ($i in 1..3) { Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 200 }
        Start-Sleep -Milliseconds 300
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
        Send-Tap 0x46;             Start-Sleep -Milliseconds 150  # F
        Send-Tap 0x44;                                             # D
        if ((Wait-NewLogLine $logPath "$($script:PfxTel)open finder=Issues" 15000) -and
            (Wait-NewLogLine $logPath "$($script:PfxTel)Focus prompt => True, mode=insert" 5000)) {
            if (Wait-OverlayForeground $vs 5000) {
                Assert-OverlayFocused $vs
                return
            }
        }
        Start-Sleep -Milliseconds 1000
    }
    throw 'Telescope issues finder did not open'
}

function Open-TelescopeReferences([object]$vs, [string]$logPath) {
    # Space F R opens the references finder (lists read/write references to the caret symbol).
    # Same focus discipline as Open-Telescope: hammer Escape first (get VsVim out of insert),
    # then wait for the overlay to own focus.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Bring-ToForeground $vs.MainWindowHandle
        foreach ($i in 1..3) { Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 200 }
        Start-Sleep -Milliseconds 300
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
        Send-Tap 0x46;             Start-Sleep -Milliseconds 150  # F
        Send-Tap 0x52;                                             # R
        if ((Wait-NewLogLine $logPath "$($script:PfxTel)open finder=References" 15000) -and
            (Wait-NewLogLine $logPath "$($script:PfxTel)Focus prompt => True, mode=insert" 5000)) {
            if (Wait-OverlayForeground $vs 5000) {
                Assert-OverlayFocused $vs
                return
            }
        }
        Start-Sleep -Milliseconds 1000
    }
    throw 'Telescope references finder did not open'
}

function Open-TelescopeGrep([object]$vs, [string]$logPath) {
    # Space F G opens the grep finder (query-driven live search over the solution's files).
    # Same focus discipline as Open-Telescope: hammer Escape first (get VsVim out of insert),
    # then wait for the overlay to own focus. NOTE: F,G is currently bound to the old
    # command:Edit.FindinFiles, so during the RED run this opens VS's Find-in-Files dialog instead.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Bring-ToForeground $vs.MainWindowHandle
        foreach ($i in 1..3) { Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 200 }
        Start-Sleep -Milliseconds 300
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
        Send-Tap 0x46;             Start-Sleep -Milliseconds 150  # F
        Send-Tap 0x47;                                             # G
        if ((Wait-NewLogLine $logPath "$($script:PfxTel)open finder=Grep" 15000) -and
            (Wait-NewLogLine $logPath "$($script:PfxTel)Focus prompt => True, mode=insert" 5000)) {
            if (Wait-OverlayForeground $vs 5000) {
                Assert-OverlayFocused $vs
                return
            }
        }
        Start-Sleep -Milliseconds 1000
    }
    throw 'Telescope grep finder did not open'
}

function Open-TelescopeImplementation([object]$vs, [string]$logPath) {
    # Space F I opens the implementation finder (lists implementations/overrides of the caret
    # symbol). Same focus discipline as Open-Telescope: hammer Escape first (get VsVim out of
    # insert), then wait for the overlay to own focus. NOTE: F,I has NO binding yet — during the
    # RED run this leader sequence does nothing and the helper's wait times out, which the
    # scenario surfaces as its first failing assertion (the right-reason RED).
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Bring-ToForeground $vs.MainWindowHandle
        foreach ($i in 1..3) { Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 200 }
        Start-Sleep -Milliseconds 300
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
        Send-Tap 0x46;             Start-Sleep -Milliseconds 150  # F
        Send-Tap 0x49;                                             # I
        if ((Wait-NewLogLine $logPath "$($script:PfxTel)open finder=Implementation" 15000) -and
            (Wait-NewLogLine $logPath "$($script:PfxTel)Focus prompt => True, mode=insert" 5000)) {
            if (Wait-OverlayForeground $vs 5000) {
                Assert-OverlayFocused $vs
                return
            }
        }
        Start-Sleep -Milliseconds 1000
    }
    throw 'Telescope implementation finder did not open'
}

function Close-Telescope([object]$vs, [string]$logPath) {
    # The overlay is a modal dialog that ALREADY owns keyboard focus — do NOT call
    # Bring-ToForeground here (it would SetForegroundWindow the VS main window and steal the
    # Escapes away from the modal). Esc in insert mode -> normal; Esc in normal mode closes.
    # Send Escape pairs (with a retry) until the "overlay closed" line appears after our cursor.
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 250
        Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 500
        if (Wait-NewLogLine $logPath "$($script:PfxTel)overlay closed" 5000) { return }
    }
    throw 'Telescope overlay did not close'
}

function Wait-NewLogLine([string]$logPath, [string]$pattern, [int]$maxMs = 20000) {
    # Searches lines appended after the per-scenario baseline (does NOT advance — an assert may
    # re-confirm a line another helper already saw). Returns true when the pattern matches.
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalMilliseconds -lt $maxMs) {
        if (Test-Path $logPath) {
            $lines = Get-Content $logPath
            $count = $lines.Count
            if ($count -gt $script:LogBaseline) {
                $tail = $lines[$script:LogBaseline..($count - 1)] -join "`n"
                if ($tail -match $pattern) { return $true }
            }
        }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

function Wait-NewLogLineAfter([string]$logPath, [int]$fromIndex, [string]$pattern, [int]$maxMs = 3000) {
    # POSITIVE bounded wait over lines appended AFTER a caller-supplied absolute line index (a
    # snapshot taken immediately BEFORE the key under test). Unlike Wait-NewLogLine it excludes
    # earlier lines, so it can attribute a new diagnostic (e.g. an editor-focus vim-mode=/editor-view
    # line) to the key just pressed. Positive wait, NOT an absence assertion.
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalMilliseconds -lt $maxMs) {
        if (Test-Path $logPath) {
            $lines = Get-Content $logPath
            if ($lines.Count -gt $fromIndex) {
                $tail = $lines[$fromIndex..($lines.Count - 1)] -join "`n"
                if ($tail -match $pattern) { return $true }
            }
        }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

function Wait-LogContains([string]$logPath, [string]$pattern, [int]$maxMs = 20000) {
    # Whole-file matcher (bootstrap/startup waits); does not use the scenario cursor.
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalMilliseconds -lt $maxMs) {
        if (Test-Path $logPath) {
            $line = Get-Content $logPath -Raw -ErrorAction SilentlyContinue
            if ($line -and $line -match $pattern) { return $true }
        }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

function Assert-NewLogLine([string]$logPath, [string]$pattern, [string]$what, [int]$maxMs = 20000) {
    if (-not (Wait-NewLogLine $logPath $pattern $maxMs)) {
        throw "never saw: $what (pattern: $pattern)"
    }
}

function Assert-NoEnterStorm([string]$logPath, [string]$what) {
    # Fail-fast (F1): the Enter/o -> OpenSelected() re-injection storm fires ~30 'solution-explorer
    # open' lines in ~100ms. A legitimate walk presses Enter/o at most once per loop iteration
    # (<=8), so >10 post-baseline open lines means the storm is present. Count WITHOUT advancing the
    # baseline (same fixed-baseline discipline as Wait-NewLogLine).
    if (-not (Test-Path $logPath)) { return }
    $lines = Get-Content $logPath
    $count = $lines.Count
    if ($count -le $script:LogBaseline) { return }
    $openCount = 0
    for ($i = $script:LogBaseline; $i -lt $count; $i++) {
        if ($lines[$i] -match "$($script:PfxNeo)solution-explorer open") { $openCount++ }
    }
    if ($openCount -gt 10) {
        throw "Enter-storm: $openCount 'solution-explorer open' lines post-baseline (bound 10) during $what"
    }
}

# ---------------------------------------------------------------------------
# Seeding consistency check (pure filesystem; used by bootstrap + seed-reset).
# ---------------------------------------------------------------------------
function Assert-SeedConsistent([string]$scratchDir) {
    # Verify every source file the harness seeds under $scratchDir has UNIFORM line endings and, for
    # the files the seeding writes, byte-identical canonical content. A mixed-EOL file (e.g.
    # TodoProbe.cs seeded with Set-Content -Value "...`n..." which appends the platform CRLF) makes
    # VS show the "normalize line endings?" modal when opened -> steals focus -> breaks scenarios.
    # Stale edits survive because seeding is gated on Test-Path; this catches them.
    # Throws on the first violation (never swallows) so the runner catch converts it into exit 1.
    $probeDir = Join-Path $scratchDir 'Probe'

    # Canonical content of the files the seeding writes (uniform CRLF; Motions.cs stays pure LF).
    $canonical = @{
        'Program.cs'            = "// Program.cs`r`n"
        'Alpha.cs'              = "// Alpha.cs`r`n"
        'Beta.cs'               = "// Beta.cs`r`n"
        'Gamma.cs'              = "// Gamma.cs`r`n"
        'Delta.cs'              = "// Delta.cs`r`n"
        'Epsilon.cs'            = "// Epsilon.cs`r`n"
        'Service.cs'            = "// Service.cs`r`n"
        'Models/User.cs'        = "// Models/User.cs`r`n"
        'Models/Order.cs'       = "// Models/Order.cs`r`n"
        'Services/AuthService.cs' = "// Services/AuthService.cs`r`n"
        'TodoProbe.cs'          = "// TODO: fix this issue`r`nclass TodoProbe { }`r`n"
        'Motions.cs'            = "class Motions`n{`n    int alpha = 1;`n    string beta = `"gamma`";`n}"  # pure LF, no trailing newline
        # Real compilable symbol graph for the references finder (telescope-references): a public
        # static field `Shared.Value` defined once and referenced from TWO other files — one read
        # site (Reader.cs) and one write site (Writer.cs) — so Roslyn find-references has an actual
        # symbol to resolve and the harness can assert read+write coverage. Uniform CRLF.
        'Models/Shared.cs'      = "class Shared`r`n{`r`n    public static int Value;`r`n}`r`n"
        'Reader.cs'             = "class Reader`r`n{`r`n    public static int Read()`r`n    {`r`n        return Shared.Value;`r`n    }`r`n}`r`n"
        'Writer.cs'             = "class Writer`r`n{`r`n    public static void Run()`r`n    {`r`n        Shared.Value = 1;`r`n    }`r`n}`r`n"
        # Distinctive marker for the grep finder (telescope-grep): "GREPME" appears on exactly TWO
        # lines of GrepProbe.cs (line 4 and line 6), so `grep hits=2` is exact and the first hit
        # (line 4) pins the preview + opened-line assertions. Uniform CRLF.
        'GrepProbe.cs'          = "// GrepProbe.cs`r`nclass GrepProbe`r`n{`r`n    // GREPME first hit line 4`r`n    int alpha = 1;`r`n    // GREPME second hit line 6`r`n    string beta = `"gamma`";`r`n}`r`n"
        # Real compilable interface->implementation graph for the implementation finder
        # (telescope-implementation): `interface IShape` declared in Models/IShape.cs (the interface
        # name `IShape` sits on line 1 starting at col 10 — a deterministic w-motion target), and
        # `class Shape : IShape` in Shape.cs implements it — its declaring line (line 2) is the
        # PINNED 1-based implementation line asserted by A5. New type names (IShape/Shape) do NOT
        # collide with the references-finder seed (Shared/Reader/Writer). Uniform CRLF.
        'Models/IShape.cs'      = "interface IShape`r`n{`r`n    void Draw();`r`n}`r`n"
        'Shape.cs'              = "// Shape.cs implementer`r`nclass Shape : IShape`r`n{`r`n    public void Draw() { }`r`n}`r`n"
    }

    # Gather every seeded source file (all *.cs plus *.sln/*.csproj) under the scratch dir.
    $seedFiles = @(
        Get-ChildItem -Path $scratchDir -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue
        Get-ChildItem -Path $scratchDir -Recurse -Filter '*.sln' -ErrorAction SilentlyContinue
        Get-ChildItem -Path $scratchDir -Recurse -Filter '*.csproj' -ErrorAction SilentlyContinue
    )
    if ($seedFiles.Count -eq 0) { throw "no seeded source files found under: $scratchDir" }

    # PASS 1 — EOL uniformity: any file with BOTH a CRLF and a lone LF is mixed.
    foreach ($f in $seedFiles) {
        $b = [System.IO.File]::ReadAllBytes($f.FullName)
        $crlf = 0; $lone = 0
        for ($i = 0; $i -lt $b.Length; $i++) {
            if ($b[$i] -eq 0x0A) {
                if ($i -gt 0 -and $b[$i - 1] -eq 0x0D) { $crlf++ } else { $lone++ }
            }
        }
        if ($crlf -gt 0 -and $lone -gt 0) {
            throw "seed file has MIXED line endings (CRLF=$crlf loneLF=$lone): $($f.FullName)"
        }
    }

    # PASS 2 — canonical content for the files the seeding writes (detects stale edits).
    foreach ($rel in $canonical.Keys) {
        $p = Join-Path $probeDir $rel
        if (-not (Test-Path $p)) { throw "seeded file missing: $p" }
        $expected = $canonical[$rel]
        $actual = [System.IO.File]::ReadAllText($p)
        if ($actual -ne $expected) {
            throw "seeded file content differs from canonical (stale edit?): $p"
        }
    }
}

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

    # Real compilable symbol graph for the references finder (telescope-references): `Shared.Value`
    # is a public static field defined in Models/Shared.cs and referenced from Reader.cs (read) and
    # Writer.cs (write). These MUST match the $canonical map in Assert-SeedConsistent byte-for-byte
    # (uniform CRLF) or the seed-consistency self-check fails the run.
    New-Item -ItemType Directory -Force -Path (Join-Path $probeDir 'Models') | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $probeDir 'Models/Shared.cs'),
        "class Shared`r`n{`r`n    public static int Value;`r`n}`r`n")
    [System.IO.File]::WriteAllText((Join-Path $probeDir 'Reader.cs'),
        "class Reader`r`n{`r`n    public static int Read()`r`n    {`r`n        return Shared.Value;`r`n    }`r`n}`r`n")
    [System.IO.File]::WriteAllText((Join-Path $probeDir 'Writer.cs'),
        "class Writer`r`n{`r`n    public static void Run()`r`n    {`r`n        Shared.Value = 1;`r`n    }`r`n}`r`n")

    # Distinctive marker for the grep finder (telescope-grep): "GREPME" on exactly TWO lines
    # (line 4 and line 6). MUST match the $canonical map byte-for-byte (uniform CRLF) or the
    # seed-consistency self-check fails the run.
    [System.IO.File]::WriteAllText((Join-Path $probeDir 'GrepProbe.cs'),
        "// GrepProbe.cs`r`nclass GrepProbe`r`n{`r`n    // GREPME first hit line 4`r`n    int alpha = 1;`r`n    // GREPME second hit line 6`r`n    string beta = `"gamma`";`r`n}`r`n")

    # Real compilable interface->implementation graph for the implementation finder
    # (telescope-implementation): `interface IShape` (Models/IShape.cs) implemented by
    # `class Shape : IShape` (Shape.cs). MUST match the $canonical map byte-for-byte (uniform CRLF).
    [System.IO.File]::WriteAllText((Join-Path $probeDir 'Models/IShape.cs'),
        "interface IShape`r`n{`r`n    void Draw();`r`n}`r`n")
    [System.IO.File]::WriteAllText((Join-Path $probeDir 'Shape.cs'),
        "// Shape.cs implementer`r`nclass Shape : IShape`r`n{`r`n    public void Draw() { }`r`n}`r`n")

    # Solution + project entry (ALWAYS, not gated on Test-Path).
    dotnet new sln -n TelescopeTest -o $scratchDir --format sln 2>&1 | Out-Null
    dotnet sln (Join-Path $scratchDir 'TelescopeTest.sln') add (Join-Path $probeDir 'Probe.csproj') 2>&1 | Out-Null
}

# ---------------------------------------------------------------------------
# Seed-leak guard (filesystem-only): generate an EXPECTED-RESULT copy of every
# seeded file at bootstrap, then prove the suite left the seed tree exactly as
# expected. Codespace scenarios (grep/references) only READ; the ONE scenario that
# intentionally writes a seed (`neovascular-editor-insert` saves typed text into
# Beta.cs) refreshes that file's expected copy at the point it validates the
# write. There is NO ignorelist: an intentional write is represented as its
# expected RESULT, and any OTHER (or later) change to any seed still fails.
# Excludes the harness's own logs/scratch — the diff is scoped to the seeded dir.
# ---------------------------------------------------------------------------
function Get-SeedFiles([string]$scratchDir) {
    # Every seeded source file (mirrors Assert-SeedConsistent's gather): *.cs + *.sln/*.csproj.
    # EXCLUDES build-output dirs (obj/ + bin/): VS/dotnet builds generate *.cs there
    # (AssemblyInfo/GlobalUsings/AssemblyAttributes) that are NOT seeds, so counting them as
    # "added" is a false leak (W22 follow-up: the guard was only self-checked, never full-run).
    # Sorted by name so the expected/diff are deterministic.
    Get-ChildItem -Path $scratchDir -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.cs', '.sln', '.csproj' } |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
        Sort-Object FullName
}

function Write-SeedExpected([string]$scratchDir, [string]$expectedDir) {
    # Materialize the bootstrap expected-result tree: a golden COPY of every seeded file, in the
    # same relative layout. seed-leak byte-compares the seed tree to this tree at the end.
    if (Test-Path $expectedDir) { Remove-Item $expectedDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $expectedDir | Out-Null
    foreach ($f in Get-SeedFiles $scratchDir) {
        $rel = $f.FullName.Substring((Resolve-Path $scratchDir).Path.Length + 1)
        $dst = Join-Path $expectedDir $rel
        New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
        Copy-Item -LiteralPath $f.FullName -Destination $dst -Force
    }
}

function Update-SeedExpected([string]$scratchDir, [string]$expectedDir, [string]$rel) {
    # Refresh ONE file's expected result after a scenario INTENTIONALLY wrote (and validated) it.
    # E.g. neovascular-editor-insert saves typed text into Beta.cs; this records the post-write
    # content as the expected RESULT. This replaces the ignorelist: nothing is skipped — the
    # expected state is simply the intended one, so any further change still fails at seed-leak.
    $src = Join-Path $scratchDir $rel
    $dst = Join-Path $expectedDir $rel
    if (-not (Test-Path $src)) { return }
    New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
    Copy-Item -LiteralPath $src -Destination $dst -Force
}

function Assert-NoSeedLeak([string]$scratchDir, [string]$expectedDir) {
    # Compare the seed tree to its expected-result tree. Reports added/removed/modified seeded
    # files as a leak. Skips gracefully (never false-fails) when the expected tree or scratch dir
    # is absent — e.g. under -NoBootstrap reuse where bootstrap did not reseed.
    if (-not (Test-Path $expectedDir)) {
        Write-Pass "seed-leak: no expected-result tree (reuse mode?) - skipped"
        return
    }
    if (-not (Test-Path $scratchDir)) {
        Write-Pass "seed-leak: scratch dir absent - skipped"
        return
    }
    $changes = @()
    foreach ($f in Get-SeedFiles $scratchDir) {
        $rel = $f.FullName.Substring((Resolve-Path $scratchDir).Path.Length + 1)
        $exp = Join-Path $expectedDir $rel
        if (-not (Test-Path $exp)) { $changes += "added: $rel"; continue }
        $actualHash = (Get-FileHash $f.FullName -Algorithm SHA256).Hash
        $expectedHash = (Get-FileHash $exp -Algorithm SHA256).Hash
        if ($actualHash -ne $expectedHash) { $changes += "modified: $rel" }
    }
    foreach ($ef in (Get-ChildItem -Path $expectedDir -Recurse -File -ErrorAction SilentlyContinue)) {
        $rel = $ef.FullName.Substring((Resolve-Path $expectedDir).Path.Length + 1)
        if (-not (Test-Path (Join-Path $scratchDir $rel))) { $changes += "removed: $rel" }
    }
    if ($changes.Count -gt 0) {
        throw "a seeded file was written into during the run (no scenario may modify the seed): $($changes -join '; ')"
    }
    Write-Pass 'seed-leak: no seeded file was modified during the run'
}

# --- telescope-open -------------------------------------------------------
Register-Scenario 'telescope-open' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-Telescope $vs $logPath
    Assert-NewLogLine $logPath "$($script:PfxTel)Focus prompt => True, mode=insert" 'prompt focused in insert mode'
    Close-Telescope $vs $logPath
}

# --- telescope-search -----------------------------------------------------
Register-Scenario 'telescope-search' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-Telescope $vs $logPath   # confirms open + focused prompt
    Assert-OverlayFocused $vs     # never type while the overlay may have lost focus
    Send-Text 'pro'
    Assert-NewLogLine $logPath "promptChanged query='pro'" "typing reached the prompt (query='pro')"
    Assert-NewLogLine $logPath "$($script:PfxTel)results count=(\d+)" 'results rendered after filter'
    Close-Telescope $vs $logPath
}

# --- telescope-navigate ---------------------------------------------------
# With many candidate files, j/k move the selection across them in NORMAL mode, and pressing i
# returns to INSERT mode (search) so you can type a new query — then j/k no longer move selection.
Register-Scenario 'telescope-navigate' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs

    # Sanity: the scratch solution must expose several files, otherwise this scenario is vacuous.
    Assert-NewLogLine $logPath "$($script:PfxTel)open finder=Files candidates=(\d+)" 'overlay listed candidates'
    $cand = 0
    $lines = Get-Content $logPath
    foreach ($ln in $lines) { if ($ln -match 'open finder=Files candidates=(\d+)') { $cand = [int]$Matches[1] } }
    if ($cand -lt 4) { throw "expected >=4 candidate files for navigation, found $cand" }

    # Insert -> normal, then j moves down one file at a time across multiple results.
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 400   # insert -> normal
    Assert-NewLogLine $logPath 'key=Escape mode=insert handled=True' 'Esc switched to normal mode'
    Send-Tap 0x4A; Start-Sleep -Milliseconds 200              # j
    Assert-NewLogLine $logPath 'results count=(\d+) selected=1' 'j moved selection to 1'
    Send-Tap 0x4A; Start-Sleep -Milliseconds 200              # j again
    Assert-NewLogLine $logPath 'results count=(\d+) selected=2' 'j moved selection to 2'
    Send-Tap 0x4B; Start-Sleep -Milliseconds 200              # k
    Assert-NewLogLine $logPath 'results count=(\d+) selected=1' 'k moved selection back to 1'
    Send-Tap 0x4B; Start-Sleep -Milliseconds 200              # k again
    Assert-NewLogLine $logPath 'results count=(\d+) selected=0' 'k moved selection back to 0'

    # G -> last, gg -> first.
    Send-Shift 0x47
    Assert-NewLogLine $logPath "results count=(\d+) selected=$($cand - 1)" 'G moved selection to the last entry'
    Send-Tap 0x47; Start-Sleep -Milliseconds 150             # g
    Send-Tap 0x47; Start-Sleep -Milliseconds 200             # g
    Assert-NewLogLine $logPath 'results count=(\d+) selected=0' 'gg moved selection back to the first entry'

    # i returns to INSERT (search) mode: the prompt becomes editable and typing filters again.
    Send-Tap 0x49; Start-Sleep -Milliseconds 300              # i
    Assert-NewLogLine $logPath 'Focus prompt => True, mode=insert' 'i returned to insert (search) mode'
    Send-Text 'alpha'
    Assert-NewLogLine $logPath "promptChanged query='alpha'" 'typing after i filtered results again'
    Assert-NewLogLine $logPath "$($script:PfxTel)results count=1 selected=0" 'alpha matched exactly one file'
    Close-Telescope $vs $logPath
}

# --- telescope-mode -------------------------------------------------------
Register-Scenario 'telescope-mode' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-Telescope $vs $logPath
    Assert-NewLogLine $logPath "$($script:PfxTel)Focus prompt => True, mode=insert" 'starts in insert mode'
    Assert-OverlayFocused $vs
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 400   # insert -> normal
    Assert-NewLogLine $logPath 'key=Escape mode=insert handled=True' 'Esc handled in insert mode'
    Send-Tap 0x49;                                              # i (back to insert)
    Assert-NewLogLine $logPath 'Focus prompt => True, mode=insert' 'i returns to insert mode'
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 400   # normal again
    Send-Tap 0x41;                                              # a (append insert)
    Assert-NewLogLine $logPath 'Focus prompt => True, mode=insert' 'a returns to insert mode'
    Close-Telescope $vs $logPath
}

# --- telescope-open-file --------------------------------------------------
Register-Scenario 'telescope-open-file' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs
    Send-Text 'Program'                                          # unique match in the scratch solution
    Assert-NewLogLine $logPath "promptChanged query='Program'" 'typed query reached prompt'
    Send-Tap $script:VkEnter; Start-Sleep -Milliseconds 800     # Enter selects -> opens file
    Assert-NewLogLine $logPath "$($script:PfxTel)opened file: .*Program\.cs" 'Enter opened Program.cs in the editor'
    Close-Telescope $vs $logPath
}

# --- neovisual-window-nav ------------------------------------------------
Register-Scenario 'neovisual-window-nav' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'window navigation (Ctrl chords)'
Send-Ctrl 0x48                                              # Ctrl+H -> navigate left
    Assert-NewLogLine $logPath "$($script:PfxNeo)shortcut-binding executed: Ctrl\+H" 'Ctrl+H fired the shortcut binding'
    Assert-NewLogLine $logPath "$($script:PfxNeo)navigate direction=L" 'Ctrl+H fired navigate left'
    Send-Ctrl 0x4C                                              # Ctrl+L -> navigate right
    Assert-NewLogLine $logPath "$($script:PfxNeo)shortcut-binding executed: Ctrl\+L" 'Ctrl+L fired the shortcut binding'
    Assert-NewLogLine $logPath "$($script:PfxNeo)navigate direction=R" 'Ctrl+L fired navigate right'
    Send-Ctrl 0x4A                                              # Ctrl+J -> navigate down
    Assert-NewLogLine $logPath "$($script:PfxNeo)shortcut-binding executed: Ctrl\+J" 'Ctrl+J fired the shortcut binding'
    Assert-NewLogLine $logPath "$($script:PfxNeo)navigate direction=D" 'Ctrl+J fired navigate down'
    Send-Ctrl 0x4B                                              # Ctrl+K -> navigate up
    Assert-NewLogLine $logPath "$($script:PfxNeo)shortcut-binding executed: Ctrl\+K" 'Ctrl+K fired the shortcut binding'
    Assert-NewLogLine $logPath "$($script:PfxNeo)navigate direction=U" 'Ctrl+K fired navigate up'
}

# --- neovisual-leader -----------------------------------------------------
Register-Scenario 'neovisual-leader' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'leader key bindings'
    Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150    # leader
    Send-Tap 0x57                                               # W -> File.SaveSelectedItems
    Assert-NewLogLine $logPath "$($script:PfxNeo)leader-binding executed: W" 'Space+W fired the W leader binding'
    Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
    Send-Tap 0x45                                               # E -> View.SolutionExplorer
    Assert-NewLogLine $logPath "$($script:PfxNeo)leader-binding executed: E" 'Space+E fired the E leader binding'
}

# --- neovisual-toolwindow ------------------------------------------------
# Solution Explorer: j/k/h/l navigate the tree (hjkl -> arrows), and i/Esc toggle input mode so
# you can type into the explorer search box.
Register-Scenario 'neovisual-toolwindow' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'tool-window navigation'
    # Ensure Solution Explorer is OPEN and focused: Space+E toggles it, and the persisted
    # experimental-instance layout may leave it open or closed from a previous run — so toggle
    # until the "toggled open" log appears (same pattern as the explorer-* scenarios).
    $opened = $false
    for ($i = 0; $i -lt 4 -and -not $opened; $i++) {
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
        Send-Tap 0x45; Start-Sleep -Milliseconds 1200
        if (Wait-NewLogLine $logPath "$($script:PfxNeo)solution-explorer toggled open" 3000) { $opened = $true }
    }
    if (-not $opened) { throw 'could not ensure Solution Explorer open' }
    Assert-NewLogLine $logPath "$($script:PfxNeo)leader-binding executed: E" 'Space+E opened Solution Explorer'
    # j/k navigate the tree (injected arrows).
    Send-Tap 0x4A
    Assert-NewLogLine $logPath "$($script:PfxNeo)toolwindow-move key=J" 'j in Solution Explorer injected a Down arrow'
    Send-Tap 0x4B
    Assert-NewLogLine $logPath "$($script:PfxNeo)toolwindow-move key=K" 'k in Solution Explorer injected an Up arrow'
    # i enters input mode (for the search box); Escape exits back to normal-mode navigation.
    Send-Tap 0x49
    Assert-NewLogLine $logPath "$($script:PfxNeo)toolwindow-enter-input" 'i entered tool-window input mode'
    Send-Tap $script:VkEscape
    Assert-NewLogLine $logPath "$($script:PfxNeo)toolwindow-exit-input" 'Escape exited tool-window input mode'
    # Back in normal mode: j navigates again.
    Send-Tap 0x4A
    Assert-NewLogLine $logPath "$($script:PfxNeo)toolwindow-move key=J" 'j navigates again after exiting input mode'
}

# --- neovisual-explorer-toggle ------------------------------------------
# Space+E toggles Solution Explorer: if closed it opens, if open it closes. Because scenarios
# share one live instance, the starting state may already be open — so we verify the toggle is a
# genuine open/close pair by pressing it until we observe a close, then confirm the next opens.
Register-Scenario 'neovisual-explorer-toggle' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'explorer toggle'

    # Press Space+E repeatedly until we've observed both a close and an open in this scenario.
    $closed = $false
    $opened = $false
    for ($i = 0; $i -lt 6 -and -not ($closed -and $opened); $i++) {
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
        Send-Tap 0x45; Start-Sleep -Milliseconds 1200
        if (Wait-NewLogLine $logPath "$($script:PfxNeo)solution-explorer toggled closed" 3000) { $closed = $true }
        if (Wait-NewLogLine $logPath "$($script:PfxNeo)solution-explorer toggled open" 3000) { $opened = $true }
    }
    if (-not $closed) { throw 'never observed Solution Explorer closing on Space+E' }
    if (-not $opened) { throw 'never observed Solution Explorer opening on Space+E' }
    Write-Pass 'Space+E toggles both open and closed'
}

# --- neovisual-explorer-open --------------------------------------------
# In Solution Explorer, l expands a collapsed folder, h collapses it, and Enter opens a file.
Register-Scenario 'neovisual-explorer-open' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'explorer open'
    # Ensure Solution Explorer is open and focused (toggle until the open log appears).
    $opened = $false
    for ($i = 0; $i -lt 4 -and -not $opened; $i++) {
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
        Send-Tap 0x45; Start-Sleep -Milliseconds 1200
        if (Wait-NewLogLine $logPath "$($script:PfxNeo)solution-explorer toggled open" 3000) { $opened = $true }
    }
    if (-not $opened) { throw 'could not ensure Solution Explorer open' }

    # Walk the tree: expand (l), step down (j), and try Enter; repeat until Enter opens something.
    # The tree is solution -> project -> files, so several expand+down steps are needed. The success
    # signal is Enter routed (`solution-explorer open`) AND the editor gaining focus right after the
    # key (a NEW `vim-mode=`/`editor-view-opened` line) — NOT the old new-text-view-only
    # `editor-view-opened` gate, which false-failed when VS REUSED an already-open tab. A tree action
    # never focuses the editor, so a genuinely FAILED Enter (no open) still fails.
    $openedView = $false
    for ($step = 0; $step -lt 8 -and -not $openedView; $step++) {
        # A previous Enter may have moved focus to the opened document; re-focus the tree so l/j
        # reach it (same pattern as neovascular-explorer-open-o).
        Focus-SolutionExplorer $vs.Id
        Start-Sleep -Milliseconds 250
        Send-Tap 0x4C; Start-Sleep -Milliseconds 250   # l -> expand current fold
        Send-Tap 0x4A; Start-Sleep -Milliseconds 250   # j -> move into the next node
        Assert-VsFocused $vs 'explorer open (Enter)'   # F16: keys must land in the VS instance
        $preKey = if (Test-Path $logPath) { (Get-Content $logPath).Count } else { 0 }
        Send-Tap $script:VkEnter; Start-Sleep -Milliseconds 400
        # Success = Enter routed (`solution-explorer open`) AND the editor gained focus right after
        # the key (a NEW `vim-mode=`/`editor-view-opened` line after the snapshot). See open-o for
        # the rationale (view-independent; detects reuse of an already-open tab).
        if ((Wait-NewLogLineAfter $logPath $preKey "$($script:PfxNeo)solution-explorer open" 1500) -and
            (Wait-NewLogLineAfter $logPath $preKey "$($script:PfxNeo)(vim-mode=|editor-view-opened)" 3000)) { $openedView = $true }
    }
    if (-not $openedView) { throw 'could not open a file from Solution Explorer (Enter was not routed, or the editor never took focus)' }
    Assert-NoEnterStorm $logPath 'neovisual-explorer-open'
    Assert-NewLogLine $logPath "$($script:PfxNeo)solution-explorer open" 'Enter fired solution-explorer open'
}

# --- neovisual-explorer-collapse ----------------------------------------
# h collapses the expanded fold (Left arrow), making the tree show fewer items.
Register-Scenario 'neovisual-explorer-collapse' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'explorer collapse'
    $opened = $false
    for ($i = 0; $i -lt 4 -and -not $opened; $i++) {
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
        Send-Tap 0x45; Start-Sleep -Milliseconds 1200
        if (Wait-NewLogLine $logPath "$($script:PfxNeo)solution-explorer toggled open" 3000) { $opened = $true }
    }
    if (-not $opened) { throw 'could not ensure Solution Explorer open' }
    # Expand first so there is something to collapse.
    Send-Tap 0x4C   # l -> expand
    Assert-NewLogLine $logPath "$($script:PfxNeo)solution-explorer expand" 'l expanded the selected fold'
    Send-Tap 0x48   # h -> collapse
    Assert-NewLogLine $logPath "$($script:PfxNeo)solution-explorer collapse" 'h collapsed the selected fold'
    Send-Tap 0x4C   # expand again so later scenarios see the files
    Assert-NewLogLine $logPath "$($script:PfxNeo)solution-explorer expand" 'l re-expanded the selected fold'
}

# --- neovisual-explorer-rename ------------------------------------------
# In Solution Explorer, r starts rename (F2 injected).
Register-Scenario 'neovisual-explorer-rename' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'explorer rename'
    $opened = $false
    for ($i = 0; $i -lt 4 -and -not $opened; $i++) {
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
        Send-Tap 0x45; Start-Sleep -Milliseconds 1200
        if (Wait-NewLogLine $logPath "$($script:PfxNeo)solution-explorer toggled open" 3000) { $opened = $true }
    }
    if (-not $opened) { throw 'could not ensure Solution Explorer open' }
    Send-Tap 0x52   # r
    Assert-NewLogLine $logPath "$($script:PfxNeo)solution-explorer rename" 'r fired solution-explorer rename'
    # F2 opened the rename text box; Escape cancels it so the tree returns to normal.
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 300
}

# --- neovisual-explorer-add ---------------------------------------------
# In Solution Explorer, a runs the Add Item command.
Register-Scenario 'neovisual-explorer-add' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'explorer add'
    $opened = $false
    for ($i = 0; $i -lt 4 -and -not $opened; $i++) {
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
        Send-Tap 0x45; Start-Sleep -Milliseconds 1200
        if (Wait-NewLogLine $logPath "$($script:PfxNeo)solution-explorer toggled open" 3000) { $opened = $true }
    }
    if (-not $opened) { throw 'could not ensure Solution Explorer open' }
    Send-Tap 0x41   # a
    Assert-NewLogLine $logPath "$($script:PfxNeo)solution-explorer add" 'a fired solution-explorer add'
    # Escape dismisses any dialog the Add Item command opened.
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 400
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 400
}

# --- neovisual-explorer-open-o ------------------------------------------
# In Solution Explorer the o key opens the selected item (the same action as Enter, per the
# SolutionExplorerController action keys — covers Run_SolutionExplorer_ActionKeys' o branch).
Register-Scenario 'neovisual-explorer-open-o' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'explorer open (o)'
    $opened = $false
    for ($i = 0; $i -lt 4 -and -not $opened; $i++) {
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
        Send-Tap 0x45; Start-Sleep -Milliseconds 1200
        if (Wait-NewLogLine $logPath "$($script:PfxNeo)solution-explorer toggled open" 3000) { $opened = $true }
    }
    if (-not $opened) { throw 'could not ensure Solution Explorer open' }

    # Walk the tree exactly like neovascular-explorer-open but open with o instead of Enter. The
    # success signal is `o` routed (`solution-explorer open`) AND the editor gaining focus right after
    # the key (a NEW `vim-mode=`/`editor-view-opened` line) — NOT the old new-text-view-only
    # `editor-view-opened` gate, which false-failed deterministically once the walk reached an
    # already-open item (VS reused the tab). A tree action (move/expand) never focuses the editor, so
    # a genuinely FAILED `o` (no open at all) still fails.
    $openedView = $false
    for ($step = 0; $step -lt 8 -and -not $openedView; $step++) {
        # A previous `o` may have moved focus to the opened document; re-focus the tree so l/j reach it.
        Focus-SolutionExplorer $vs.Id
        Start-Sleep -Milliseconds 250
        Send-Tap 0x4C; Start-Sleep -Milliseconds 250   # l -> expand current fold
        Send-Tap 0x4A; Start-Sleep -Milliseconds 250   # j -> move into the next node
        Assert-VsFocused $vs 'explorer open (o)'       # F16: keys must land in the VS instance
        $preKey = if (Test-Path $logPath) { (Get-Content $logPath).Count } else { 0 }
        Send-Tap 0x4F; Start-Sleep -Milliseconds 400   # o -> open
        # Success = o routed (existing `solution-explorer open` diagnostic) AND the editor GAINED
        # FOCUS right after the key (a NEW `vim-mode=`/`editor-view-opened` line after the snapshot).
        # The focus line is view-independent — it also fires when VS REUSES an already-open tab,
        # which is what the old editor-view-opened-only gate false-failed on — and a tree action
        # never focuses the editor. A genuinely FAILED o (not routed / never opens anything) fails.
        if ((Wait-NewLogLineAfter $logPath $preKey "$($script:PfxNeo)solution-explorer open" 1500) -and
            (Wait-NewLogLineAfter $logPath $preKey "$($script:PfxNeo)(vim-mode=|editor-view-opened)" 3000)) { $openedView = $true }
    }
    if (-not $openedView) { throw 'could not open a file from Solution Explorer with o (o was not routed, or the editor never took focus)' }
    Assert-NoEnterStorm $logPath 'neovisual-explorer-open-o'
    Assert-NewLogLine $logPath "$($script:PfxNeo)solution-explorer open" 'o fired solution-explorer open'
}

# --- neovisual-explorer-move --------------------------------------------
# In Solution Explorer the m key runs the Move command (covers Run_SolutionExplorer_ActionKeys' m
# branch). The Move dialog that opens is dismissed with Escape so the tree stays navigable.
Register-Scenario 'neovisual-explorer-move' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'explorer move'
    $opened = $false
    for ($i = 0; $i -lt 4 -and -not $opened; $i++) {
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
        Send-Tap 0x45; Start-Sleep -Milliseconds 1200
        if (Wait-NewLogLine $logPath "$($script:PfxNeo)solution-explorer toggled open" 3000) { $opened = $true }
    }
    if (-not $opened) { throw 'could not ensure Solution Explorer open' }
    Send-Tap 0x4D   # m
    Assert-NewLogLine $logPath "$($script:PfxNeo)solution-explorer move" 'm fired solution-explorer move'
    # The Move dialog opened; Escape dismisses it.
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 400
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 400
}

# --- neovisual-explorer-move-editor-focus --------------------------------
# Negative half of the regression pair: with an EDITOR focused and the stale-frame fault injected
# (a 'stale-toolwindow' sentinel makes WindowManager report the SE frame as current), m must fall
# through to VS — it must NOT fire a solution-explorer action. Deterministic: the fault is a file
# presence toggle, and the post-baseline absence scan is bounded by the Space+W leader line, which
# is written only after m was handled on the same UI thread.
Register-Scenario 'neovisual-explorer-move-editor-focus' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath

    # 1. Open Gamma.cs through the Telescope overlay so the editor holds keyboard focus. Use Gamma.cs
    #    (NOT Program.cs, NOT Beta.cs): Program.cs is the startup file and is left open by earlier
    #    scenarios; Beta.cs is reserved for the later neovascular-editor-insert scenario, which needs
    #    it to open a FRESH view (opening it here would reuse the tab and break that assertion).
    #    Gamma.cs is opened by no other scenario. Focus is proven by querying the ACTIVE DOCUMENT via
    #    DTE (view-independent) instead of asserting a new text view — VS reuses an already-open tab
    #    without raising `editor-view-opened`.
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs
    Send-Text 'Gamma'
    Assert-NewLogLine $logPath "promptChanged query='Gamma'" 'typed query reached prompt'
    Assert-NewLogLine $logPath "$($script:PfxTel)results count=1 selected=0" 'filter settled to the single Gamma.cs match'
    Assert-NewLogLine $logPath "$($script:PfxTel)preview file=.*Gamma\.cs" 'preview shows the Gamma.cs match'
    Send-Tap $script:VkEnter; Start-Sleep -Milliseconds 800
    Assert-NewLogLine $logPath "$($script:PfxTel)opened file: .*Gamma\.cs" 'Enter opened Gamma.cs'
    $activeDoc = Wait-ActiveDocumentMatch $vs.Id 'Gamma\.cs$' 3000
    if (-not $activeDoc) { throw 'editor did not show Gamma.cs after opening it (active document never matched Gamma.cs)' }
    Close-Telescope $vs $logPath

    Enter-NormalContext $vs
    Assert-VsFocused $vs 'editor-focused m'

    # 2. Inject the stale-frame fault for the duration of the key sequence only.
    $sentinel = Join-Path (Split-Path $logPath) 'stale-toolwindow'
    New-Item -ItemType File -Force -Path $sentinel | Out-Null
    try {
        # 3. m must fall through to the editor (no tree action). Escape dismisses any dialog on the
        #    old path; then Space+W must still reach the editor and fire the leader binding — the
        #    positive bound proving focus stayed in the editor.
        Send-Tap 0x4D                                            # m
        Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 200 # dismiss any old-path dialog
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150  # leader
        Send-Tap 0x57; Start-Sleep -Milliseconds 500             # W -> File.SaveSelectedItems
        Assert-NewLogLine $logPath "$($script:PfxNeo)leader-binding executed: W" 'editor kept focus; m was not a tree action'
    } finally {
        Remove-Item -Force -LiteralPath $sentinel -ErrorAction SilentlyContinue
    }

    # 4. Deterministic absence scan of the fixed post-baseline window for a tree move action. The
    #    bound line is already on disk and MoveSelected logs synchronously before any later key, so
    #    a move line from m would already be present — its absence is a fact, not a race.
    if (-not (Test-Path $logPath)) { throw 'log missing for editor-focus absence scan' }
    $lines = Get-Content $logPath
    for ($i = $script:LogBaseline; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "$($script:PfxNeo)solution-explorer move") {
            throw "editor-focused m leaked a tree action: $($lines[$i])"
        }
    }
}

# --- explorer-open-navigation --------------------------------------------
# Solution Explorer programmatic tree selection: `g` selects the FIRST physical source file under
# the solution's project via DTE UIHierarchy (no key injection -> no csproj-open trap), logging a
# truthful `solution-explorer select file=...` diagnostic; `o` then opens that file. Distinct from
# neovisual-explorer-open/-open-o (which loop l/j/open-until-something-opens) because it depends on
# a deterministic programmatic selection, not the visual tree's expansion state.
Register-Scenario 'explorer-open-navigation' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'explorer navigation'
    # Ensure Solution Explorer is open and focused (toggle until the open log appears).
    $opened = $false
    for ($i = 0; $i -lt 4 -and -not $opened; $i++) {
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
        Send-Tap 0x45; Start-Sleep -Milliseconds 1200
        if (Wait-NewLogLine $logPath "$($script:PfxNeo)solution-explorer toggled open" 3000) { $opened = $true }
    }
    if (-not $opened) { throw 'could not ensure Solution Explorer open' }

    # g -> programmatically select the first physical source file under the project (UIHierarchy
    # walk; escapes the injected-key/csproj-open trap), SELECT it in the tree, and OPEN it in the
    # editor. `g` deliberately opens (and emits `editor-view-opened file=`) so the deterministic
    # selection is observable even when the target file is already open (activating an existing
    # view raises no TextViewCreated) — the assertion below proves `g` reached the controller with
    # the real selected path, and is order-independent of `o` (which then opens what `o` selects).
    Send-Tap 0x47; Start-Sleep -Milliseconds 800   # g -> select + open first source file
    Assert-NewLogLine $logPath "$($script:PfxNeo)solution-explorer select file=.*\.cs" 'g selected the first source file'
    Assert-NewLogLine $logPath "$($script:PfxNeo)editor-view-opened file=.*\.cs" 'g opened the selected source file'
    Assert-VsFocused $vs 'explorer navigation (o)' # keys must land in the VS instance
    Send-Tap 0x4F; Start-Sleep -Milliseconds 800   # o -> open the selected item (tree focus intact)
    Assert-NewLogLine $logPath "$($script:PfxNeo)solution-explorer open" 'o fired solution-explorer open'
    Assert-NoEnterStorm $logPath 'explorer-open-navigation'
}

# --- explorer-open-searchbox ---------------------------------------------
# Solution Explorer search box: i focuses the search box (and enters input mode), typing filters the
# tree natively, Escape returns focus to the (now-filtered) tree, and o opens the single filtered
# result. Covers the search-box focus path + the ExitInputMode refocus (F16's earlier concern).
Register-Scenario 'explorer-open-searchbox' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'explorer search box'
    # Ensure Solution Explorer is open and focused (toggle until the open log appears).
    $opened = $false
    for ($i = 0; $i -lt 4 -and -not $opened; $i++) {
        Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150
        Send-Tap 0x45; Start-Sleep -Milliseconds 1200
        if (Wait-NewLogLine $logPath "$($script:PfxNeo)solution-explorer toggled open" 3000) { $opened = $true }
    }
    if (-not $opened) { throw 'could not ensure Solution Explorer open' }

    # i focuses the search box and enters input mode.
    Send-Tap 0x49; Start-Sleep -Milliseconds 400   # i -> search box focus + input mode
    Assert-NewLogLine $logPath "$($script:PfxNeo)solution-explorer search-focus" 'i focused the Solution Explorer search box'
    Assert-NewLogLine $logPath "$($script:PfxNeo)toolwindow-enter-input" 'i entered tool-window input mode'
    # Type a query that filters the tree to a single file (native live filtering).
    Send-Text 'GrepProbe'
    Start-Sleep -Milliseconds 500
    # Escape exits input mode and refocuses the tree (View.SolutionExplorer); the native filter
    # keeps the single GrepProbe.cs result selected.
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 400
    Assert-NewLogLine $logPath "$($script:PfxNeo)toolwindow-exit-input" 'Escape exited input mode'
    # o (normal mode, tree focused) opens the filtered result.
    Assert-VsFocused $vs 'explorer search box (o)'
    Send-Tap 0x4F; Start-Sleep -Milliseconds 800   # o -> open the filtered result
    Assert-NewLogLine $logPath "$($script:PfxNeo)solution-explorer open" 'o fired solution-explorer open'
    Assert-NewLogLine $logPath "$($script:PfxNeo)editor-view-opened file=.*[\\/]GrepProbe\.cs" 'o opened the filtered result (GrepProbe.cs)'
    Assert-NoEnterStorm $logPath 'explorer-open-searchbox'
}

# --- telescope-wrap ------------------------------------------------------
# Selection wraps around the result list: j past the last goes to 0, k past the first goes to last.
Register-Scenario 'telescope-wrap' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs
    $lines = Get-Content $logPath
    $cand = 0
    foreach ($ln in $lines) { if ($ln -match 'open finder=Files candidates=(\d+)') { $cand = [int]$Matches[1] } }
    if ($cand -lt 3) { throw "expected >=3 candidates for wrap test, found $cand" }

    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 400   # normal mode
    # k from the first entry wraps to the last; j at the last wraps to the first.
    Send-Tap 0x4B
    Assert-NewLogLine $logPath "results count=(\d+) selected=$($cand - 1)" 'k at index 0 wrapped to the last entry'
    Send-Tap 0x4A
    Assert-NewLogLine $logPath 'results count=(\d+) selected=0' 'j at the last entry wrapped to 0'
    Close-Telescope $vs $logPath
}

# --- telescope-preview ---------------------------------------------------
# The selected file's content shows in the preview pane; Ctrl+L moves focus into the preview
# where vim motions navigate the code read-only; Ctrl+H returns to the list.
Register-Scenario 'telescope-preview' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs

    # The selected (first) candidate must be a real file so the preview has content.
    Assert-NewLogLine $logPath "$($script:PfxTel)preview file=.*\.cs" 'preview loaded the selected file content'
    # The preview renders the file as syntax-highlighted tokens (>= 1 token proves the
    # SyntaxHighlighter ran and colored the content).
    Assert-NewLogLine $logPath "$($script:PfxTel)preview tokens=\d+" 'preview rendered syntax-highlighted tokens'
    # Ctrl+L moves focus to the preview.
    Send-Ctrl 0x4C
    Assert-NewLogLine $logPath 'focus target=Preview' 'Ctrl+L moved focus to the preview'
    # j moves the preview caret down a line (vim motion over the code).
    Send-Tap 0x4A
    Assert-NewLogLine $logPath "$($script:PfxTel)preview caret=\d+ line=2" 'j moved the preview caret to line 2'
    # Escape returns to the list.
    Send-Tap $script:VkEscape
    Assert-NewLogLine $logPath 'focus target=List' 'Escape returned focus to the list'
    Close-Telescope $vs $logPath
}

# --- neovisual-editor-insert ---------------------------------------------
# Regression guard: the global hook must NOT swallow/steal typed characters while the VsVim
# editor is in INSERT mode. This is the risk area — IsInteresting marks h/j/k/l/i/Space/Escape
# as interesting, and the leader key (Space) + hjkl/I routing must all pass through when the
# focused editor is in a typing mode (VimModeTracker.IsInTypingMode). We type a string that
# contains those very keys and prove every character lands in the saved file on disk.
Register-Scenario 'neovisual-editor-insert' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath

    # Open Beta.cs (NOT auto-opened at startup — Program.cs is) through the Telescope overlay, so
    # a brand-new editor view is created for it and the editor gets focus.
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs
    Send-Text 'Beta'
    Assert-NewLogLine $logPath "promptChanged query='Beta'" 'typed query reached prompt'
    # The filter is async — WAIT for it to render before pressing Enter, otherwise the selection
    # is still on the first (unfiltered) candidate and Enter opens the wrong file.
    Assert-NewLogLine $logPath "$($script:PfxTel)results count=1 selected=0" 'filter rendered the single Beta.cs match'
    Assert-NewLogLine $logPath "$($script:PfxTel)preview file=.*Beta\.cs" 'preview shows the Beta.cs match'
    Send-Tap $script:VkEnter; Start-Sleep -Milliseconds 800
    Assert-NewLogLine $logPath "$($script:PfxTel)opened file: .*Beta\.cs" 'Enter opened Beta.cs'
    Assert-NewLogLine $logPath "$($script:PfxNeo)editor-view-opened file=.*Beta\.cs" 'editor view for Beta.cs created'
    Close-Telescope $vs $logPath

    # The editor has Beta.cs focused. Get VsVim into NORMAL mode, then press i to enter INSERT.
    # CRITICAL: move the caret INTO the seeded line first (`// Beta.cs`, a comment). At the document
    # start (col 1, BEFORE the `//`) the caret is in CODE context, where VS C# IntelliSense auto-pops
    # statement completion on the first identifier char: typing `hi` selects the camel-case match
    # `HandleInheritability`, and the next injected Space commits that item, replacing `hi` with
    # `HandleInheritability` (the observed `HandleInheritability jk <run>// Beta.cs` corruption). `w`
    # is a VsVim normal-mode word motion (0x57) — not hook-interesting, so it falls straight through
    # to VsVim — which advances the caret to `Beta`, inside the comment. In comment context
    # completion never triggers, so the marker lands verbatim; the file check still proves
    # insert-mode typing was not swallowed.
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'editor insert-mode typing'
    Send-Tap 0x57; Start-Sleep -Milliseconds 200                     # w -> move caret into the // comment
    Send-Tap 0x57; Start-Sleep -Milliseconds 200                     # w -> further inside (never before the //)
    Send-Tap 0x49; Start-Sleep -Milliseconds 400                     # i -> insert mode (inside the comment)
    Assert-NewLogLine $logPath "$($script:PfxNeo)vim-mode=Insert" 'i switched the editor into insert mode'

    # Type a marker containing the "interesting" keys (h, i, j, k, and a Space). Each key is
    # marked interesting by the hook pre-filter, so any swallow/misroute here breaks the file check.
    $run = [regex]::Match((Split-Path $logPath -Leaf), '^(\d+)').Groups[1].Value
    $marker = "hi jk $run"
    Send-Text $marker
    Start-Sleep -Milliseconds 300

    # Exit insert mode and save the buffer; the file content is the ground-truth proof the text
    # reached the editor (a swallowed Space/hjkl/i would have made the marker partial).
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 300         # insert -> normal
    Assert-NewLogLine $logPath "$($script:PfxNeo)vim-mode=Normal" 'Esc switched the editor back to normal mode'
    Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150          # leader
    Send-Tap 0x57; Start-Sleep -Milliseconds 1000                    # W -> File.SaveSelectedItems
    Assert-NewLogLine $logPath "$($script:PfxNeo)leader-binding executed: W" 'Space+W saved the file'

    $probeDir = Join-Path (Join-Path $env:TEMP 'telescope_scratch') 'Probe'
    $file = Join-Path $probeDir 'Beta.cs'
    # This scenario INTENTIONALLY writes Beta.cs (the Space+W save above). Record the observed
    # post-save content as the expected RESULT **in a finally** — i.e. atomically with the write,
    # before any assertion can throw. This keeps the no-ignorelist seed-leak guard coupled to the
    # ACTUAL write rather than to this scenario's success: if this scenario flakes (its own
    # known-RED marker assertion throws), seed-leak must still see the expected post-save content,
    # not a stale bootstrap copy. Any OTHER or LATER change to any seed (including Beta.cs) still
    # fails at seed-leak.
    try {
        if (-not (Test-Path $file)) { throw "editor file missing: $file" }
        $content = Get-Content $file -Raw -ErrorAction SilentlyContinue
        if (-not $content) { throw 'editor file is empty after save' }
        if (-not $content.Contains($marker)) {
            $preview = if ($content.Length -gt 200) { $content.Substring(0, 200) } else { $content }
            throw "typed text was swallowed/not inserted (file lacks marker '$marker'). Content: $preview"
        }
        Write-Pass "typed text reached the editor (file contains '$marker')"
    } finally {
        $scratchRoot = Split-Path $probeDir -Parent
        Update-SeedExpected $scratchRoot (Join-Path $logDir 'seed-expected') 'Probe\Beta.cs'
    }
}

# --- neovisual-textinput-motions -----------------------------------------
# Vim text motions in a text-input tool window (Command Window): after Esc (exit insert) it stays
# in the text input and h/l/w/b/e move the caret (swallowed, not typed), while A/I enter insert at
# the end/start of the line. The caret positions are logged by the controller and asserted below.
Register-Scenario 'neovisual-textinput-motions' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'text-input tool window motions'

    # Open the Command Window (a text-input tool window) via the Space+C W leader binding (wired to
    # View.CommandWindow in default-keybindings.json). Opening is async and focus lags by a few
    # seconds, so poll with Escapes until one lands while the window's input is in insert mode
    # (logged as toolwindow-exit-input). NEVER retry the leader sequence itself — once the window
    # is open, re-sending Space/C/W would type into its input.
    Send-Tap $script:VkSpace; Start-Sleep -Milliseconds 150        # leader
    Send-Tap 0x43; Start-Sleep -Milliseconds 150                   # C
    Send-Tap 0x57                                                  # W -> View.CommandWindow
    $opened = $false
    for ($i = 0; $i -lt 8 -and -not $opened; $i++) {
        Start-Sleep -Milliseconds 800
        Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 250
        if (Wait-NewLogLine $logPath "$($script:PfxNeo)toolwindow-exit-input" 1200) { $opened = $true }
    }
    if (-not $opened) { throw 'Command Window did not open and take focus' }
    # Normal mode in a text-input window draws a BLOCK caret over the editor view.
    Assert-NewLogLine $logPath "$($script:PfxNeo)block-caret active=True" 'normal mode shows the block caret'

    # Back to insert mode (generic i), then type a known string. The Command Window's editor
    # buffer includes the '>' prompt, so 'hello' yields text '>hello' (length 6, caret 6).
    Send-Tap 0x49; Start-Sleep -Milliseconds 300                       # i
    Assert-NewLogLine $logPath "$($script:PfxNeo)toolwindow-enter-input" 'i re-entered insert mode'
    Assert-NewLogLine $logPath "$($script:PfxNeo)block-caret active=False" 'insert mode restores the line caret'
    Send-Text 'hello'; Start-Sleep -Milliseconds 300
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 300           # insert -> normal
    Assert-NewLogLine $logPath "$($script:PfxNeo)toolwindow-exit-input" 'Esc left insert mode with hello typed'

    # h/l move by character over '>hello' (caret 6 -> 5, then back), swallowed (never typed).
    Send-Tap 0x48; Start-Sleep -Milliseconds 200                       # h
    Assert-NewLogLine $logPath "$($script:PfxNeo)text-motion key=H caret=5" 'h moved the caret left to 5'
    # a (bare a) enters insert AFTER the caret: from caret 5 the caret lands at 6 (after 'o').
    Send-Tap 0x41; Start-Sleep -Milliseconds 250                       # a
    Assert-NewLogLine $logPath "$($script:PfxNeo)textinput-enter-input after caret=6" 'a entered insert after the caret'
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 300           # insert -> normal
    Assert-NewLogLine $logPath "$($script:PfxNeo)toolwindow-exit-input" 'Esc left the a-insert'
    Send-Tap 0x4C; Start-Sleep -Milliseconds 200                       # l
    Assert-NewLogLine $logPath "$($script:PfxNeo)text-motion key=L caret=6" 'l moved the caret right to 6'

    # w -> end of the single word, b -> back to the start (before the prompt), e -> end again.
    Send-Tap 0x57; Start-Sleep -Milliseconds 200                       # w
    Assert-NewLogLine $logPath "$($script:PfxNeo)text-motion key=W caret=6" 'w moved to the end of the word'
    Send-Tap 0x42; Start-Sleep -Milliseconds 200                       # b
    Assert-NewLogLine $logPath "$($script:PfxNeo)text-motion key=B caret=0" 'b moved back to the start'
    Send-Tap 0x45; Start-Sleep -Milliseconds 200                       # e
    Assert-NewLogLine $logPath "$($script:PfxNeo)text-motion key=E caret=6" 'e moved to the end of the word'

    # A (Shift+a) enters insert at the END of the line; typing appends.
    Send-Shift 0x41; Start-Sleep -Milliseconds 250                     # A
    Assert-NewLogLine $logPath "$($script:PfxNeo)textinput-enter-input end caret=6" 'A entered insert at the line end'
    Send-Text ' world'; Start-Sleep -Milliseconds 300
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 300

    # I (Shift+i) enters insert at the START of the line (caret 0). Note: the Command Window's '>'
    # prompt is a read-only region, so typing there is rejected — we just verify the caret jumps.
    Send-Shift 0x49; Start-Sleep -Milliseconds 250                     # I
    Assert-NewLogLine $logPath "$($script:PfxNeo)textinput-enter-input start caret=0" 'I entered insert at the line start'
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 300

    # Text is now '>hello world': w from the start jumps to the next word, e to its end, A
    # appends, and b jumps back to the word start.
    Send-Tap 0x57; Start-Sleep -Milliseconds 200                       # w
    Assert-NewLogLine $logPath "$($script:PfxNeo)text-motion key=W caret=7" 'w jumped to the next word (world)'
    Send-Tap 0x45; Start-Sleep -Milliseconds 200                       # e
    Assert-NewLogLine $logPath "$($script:PfxNeo)text-motion key=E caret=12" 'e jumped to the end of world'
    Send-Shift 0x41; Start-Sleep -Milliseconds 250                     # A
    Assert-NewLogLine $logPath "$($script:PfxNeo)textinput-enter-input end caret=12" 'A entered insert at the end again'
    Send-Text '!'; Start-Sleep -Milliseconds 300
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 300           # insert -> normal
    Send-Tap 0x42; Start-Sleep -Milliseconds 200                       # b
    Assert-NewLogLine $logPath "$($script:PfxNeo)text-motion key=B caret=7" 'b moved back to the start of world'

    Write-Pass 'text-input window vim motions moved the caret and entered insert at A/I positions'
}

# --- telescope-issues ----------------------------------------------------
# The code-issues finder (Space F D) lists warnings/errors/TODO markers. A seeded TODO in
# TodoProbe.cs is found, filtered, previewed (jumping to the issue line), and opened at that line.
Register-Scenario 'telescope-issues' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-TelescopeIssues $vs $logPath
    Assert-OverlayFocused $vs
    Assert-NewLogLine $logPath "$($script:PfxTel)open finder=Issues candidates=(\d+)" 'issues finder listed candidates'

    # Filter to the seeded TODO marker.
    Send-Text 'fix this'
    Assert-NewLogLine $logPath "promptChanged query='fix this'" 'typed query reached prompt'
    Assert-NewLogLine $logPath "$($script:PfxTel)results count=\d+ selected=0" 'TODO marker ranked first (count varies with Error List noise)'
    # The preview loads the issue file and jumps the caret to the TODO line (line 1).
    Assert-NewLogLine $logPath "$($script:PfxTel)preview file=.*TodoProbe\.cs" 'preview shows the issue file'
    Assert-NewLogLine $logPath "$($script:PfxTel)preview caret=\d+ line=1" 'preview caret jumped to the issue line'

    # Enter opens the file and jumps to the TODO line.
    Send-Tap $script:VkEnter; Start-Sleep -Milliseconds 800
    Assert-NewLogLine $logPath "$($script:PfxTel)opened issue: .*TodoProbe\.cs line=\d+" 'Enter opened the issue at its line'
    Close-Telescope $vs $logPath
}

# --- telescope-references -------------------------------------------------
# The references finder (Space F R) lists every reference to the symbol under the caret, showing
# read/write access. The scratch solution seeds a public static field `Shared.Value` (defined in
# Models/Shared.cs) with a READ site (Reader.cs) and a WRITE site (Writer.cs). The scenario opens
# the defining file, positions the caret on `Value`, opens the references finder, and asserts the
# candidates count (>=2), the read/write gather summary, the preview line-jump, and that Enter
# opens a reference at its line with its access kind.
Register-Scenario 'telescope-references' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath

    # Step 1: open the defining file (Models/Shared.cs) via the overlay.
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs
    Send-Text 'Shared'
    Assert-NewLogLine $logPath "promptChanged query='Shared'" 'typed query reached prompt'
    Assert-NewLogLine $logPath "$($script:PfxTel)results count=1 selected=0" 'filter rendered the single Shared.cs match'
    Assert-NewLogLine $logPath "$($script:PfxTel)preview file=.*Shared\.cs" 'preview shows the Shared.cs match'
    Send-Tap $script:VkEnter; Start-Sleep -Milliseconds 800
    Assert-NewLogLine $logPath "$($script:PfxTel)opened file: .*Shared\.cs" 'Enter opened Models/Shared.cs'
    Close-Telescope $vs $logPath

    # Step 2: position the caret on the `Value` field symbol with deterministic VsVim normal-mode
    # motions. Shared.cs is:
    #   1: class Shared
    #   2: {
    #   3:     public static int Value;   <- cols: public=4, static=11, int=18, Value=22
    #   4: }
    # After opening, the caret is line 1 col 0. j,j descend to line 3 (col 0); w x4 walks
    # public -> static -> int -> Value (start of the symbol).
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'references caret positioning'
    Send-Tap 0x4A; Start-Sleep -Milliseconds 200   # j -> line 2
    Send-Tap 0x4A; Start-Sleep -Milliseconds 200   # j -> line 3 (the field line)
    Send-Tap 0x57; Start-Sleep -Milliseconds 200   # w -> public
    Send-Tap 0x57; Start-Sleep -Milliseconds 200   # w -> static
    Send-Tap 0x57; Start-Sleep -Milliseconds 200   # w -> int
    Send-Tap 0x57; Start-Sleep -Milliseconds 200   # w -> Value

    # Step 3: Space+F R -> references finder, >=2 candidates (definition + read + write vs the
    # current filter; candidates never logged per-row, only the count).
    Open-TelescopeReferences $vs $logPath
    Assert-OverlayFocused $vs
    Assert-NewLogLine $logPath "$($script:PfxTel)open finder=References candidates=(\d+)" 'references finder listed candidates'
    $cand = 0
    $lines = Get-Content $logPath
    foreach ($ln in $lines) { if ($ln -match 'open finder=References candidates=(\d+)') { $cand = [int]$Matches[1] } }
    if ($cand -lt 2) { throw "expected >=2 references, found $cand" }

    # Step 4: gather summary proves read AND write coverage (seeded read + write sites).
    Assert-NewLogLine $logPath "$($script:PfxTel)references gathered reads=(\d+) writes=(\d+)" 'references gather summary logged'
    $reads = 0; $writes = 0
    foreach ($ln in (Get-Content $logPath)) {
        if ($ln -match 'references gathered reads=(\d+) writes=(\d+)') { $reads = [int]$Matches[1]; $writes = [int]$Matches[2] }
    }
    if ($reads -lt 1) { throw "expected >=1 read reference, found $reads" }
    if ($writes -lt 1) { throw "expected >=1 write reference, found $writes" }

    # Step 5: preview jumps to the selected reference line.
    Assert-NewLogLine $logPath "$($script:PfxTel)preview file=.*\.cs" 'preview loaded the reference file'
    Assert-NewLogLine $logPath "$($script:PfxTel)preview caret=\d+ line=\d+" 'preview caret jumped to the reference line'

    # Step 6: Enter opens the reference at its line with its read/write access.
    Send-Tap $script:VkEnter; Start-Sleep -Milliseconds 800
    Assert-NewLogLine $logPath "$($script:PfxTel)opened reference: file=.*\.cs line=\d+ col=\d+ access=(read|write)" 'Enter opened the reference with access kind'

    # Step 7: close.
    Close-Telescope $vs $logPath
}

# --- telescope-implementation ----------------------------------------------
# The implementation finder (Space F I) lists the implementations/overrides of the symbol under
# the caret. The scratch solution seeds a NEW interface `IShape` (Models/IShape.cs) implemented by
# `class Shape : IShape` (Shape.cs) — new type names that do NOT collide with the references-finder
# seed (Shared). The scenario opens the interface file, positions the caret on `IShape`, opens the
# implementation finder, and asserts the candidates count, the gather summary, the preview
# line-jump to the pinned implementation line, and that Enter opens that line.
Register-Scenario 'telescope-implementation' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath

    # Step 1: open the interface file (Models/IShape.cs) via the overlay.
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs
    Send-Text 'IShape'
    Assert-NewLogLine $logPath "promptChanged query='IShape'" 'typed query reached prompt'
    Assert-NewLogLine $logPath "$($script:PfxTel)results count=1 selected=0" 'filter rendered the single IShape.cs match'
    Assert-NewLogLine $logPath "$($script:PfxTel)preview file=.*IShape\.cs" 'preview shows the IShape.cs match'
    Send-Tap $script:VkEnter; Start-Sleep -Milliseconds 800
    Assert-NewLogLine $logPath "$($script:PfxTel)opened file: .*IShape\.cs" 'Enter opened Models/IShape.cs'
    Close-Telescope $vs $logPath

    # Step 2: position the caret on the `IShape` interface name with deterministic VsVim
    # normal-mode motions. IShape.cs is:
    #   1: interface IShape   <- IShape starts at col 10 (after the 9-char 'interface' + 1 space)
    #   2: {
    #   3:     void Draw();
    #   4: }
    # After opening, the caret is line 1 col 0; w walks to the start of the IShape token (col 10).
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'implementation caret positioning'
    Send-Tap 0x57; Start-Sleep -Milliseconds 200   # w -> IShape

    # Step 3: Space+F I -> implementation finder, >=1 candidate (the Shape implementation).
    Open-TelescopeImplementation $vs $logPath
    Assert-OverlayFocused $vs
    Assert-NewLogLine $logPath "$($script:PfxTel)open finder=Implementation candidates=(\d+)" 'implementation finder listed candidates'
    $cand = 0
    $lines = Get-Content $logPath
    foreach ($ln in $lines) { if ($ln -match 'open finder=Implementation candidates=(\d+)') { $cand = [int]$Matches[1] } }
    if ($cand -lt 1) { throw "expected >=1 implementation candidate, found $cand" }
    Assert-NewLogLine $logPath "$($script:PfxTel)implementations gathered count=(\d+)" 'implementations gather summary logged'

    # Step 4: preview loads the implementation file and jumps the caret to the pinned declaring
    # line (`class Shape : IShape` on line 2 of Shape.cs).
    Assert-NewLogLine $logPath "$($script:PfxTel)preview file=.*Shape\.cs" 'preview loaded the implementation file'
    Assert-NewLogLine $logPath "$($script:PfxTel)preview caret=\d+ line=2" 'preview caret jumped to the implementation line'

    # Step 5: Enter opens the file at the SPECIFIC pinned implementation line (line 2). This is
    # the only finder that injects Enter with no intervening typing, so gate it explicitly on the
    # overlay being the REAL OS foreground window (PID + window text "Telescope") — not just the
    # VS PID, which also matches the main window and let Enter race OS activation.
    Assert-OverlayFocused $vs
    Send-Tap $script:VkEnter; Start-Sleep -Milliseconds 800
    Assert-NewLogLine $logPath "$($script:PfxTel)opened implementation: file=.*Shape\.cs line=2" 'Enter opened the implementation at line 2'

    # Step 6: close.
    Close-Telescope $vs $logPath
}

# --- telescope-grep -------------------------------------------------------
# The grep finder (Space F G) live-searches the solution's source files for the typed query
# (query-driven, case-insensitive substring — NOT fzf-filtered), shows each hit's file/line/text,
# previews the hit file with the caret jumped to the hit line, and opens the file at that line on
# Enter. The scratch solution seeds a distinctive marker "GREPME" on exactly TWO lines of
# GrepProbe.cs (line 4 and line 6), so `grep hits=2` is exact and the first hit (line 4) pins the
# preview + opened-line assertions end-to-end.
Register-Scenario 'telescope-grep' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-TelescopeGrep $vs $logPath
    Assert-OverlayFocused $vs

    # Step 1: empty query -> deterministic 0 candidates.
    Assert-NewLogLine $logPath "$($script:PfxTel)open finder=Grep candidates=0" 'grep finder opened with empty query -> 0 candidates'

    # Step 2: type the token. The debounce re-runs the query-driven gather after typing settles,
    # logging exactly one 'grep hits=2' for the two seeded GREPME lines (harness polls for seconds,
    # so the settled count is asserted, not per-keystroke).
    Send-Text 'GREPME'
    Assert-NewLogLine $logPath "$($script:PfxTel)grep hits=2" 'grep found the 2 seeded GREPME lines'

    # Step 3: preview jumps to the first hit's file/line (GrepProbe.cs, line 4).
    Assert-NewLogLine $logPath "$($script:PfxTel)preview file=.*GrepProbe\.cs" 'preview shows the grep hit file'
    Assert-NewLogLine $logPath "$($script:PfxTel)preview caret=\d+ line=4" 'preview caret jumped to the first hit line'

    # Step 4: Enter opens the file at the pinned hit line (line 4).
    Send-Tap $script:VkEnter; Start-Sleep -Milliseconds 800
    Assert-NewLogLine $logPath "$($script:PfxTel)opened grep: file=.*GrepProbe\.cs line=4" 'Enter opened the grep hit at line 4'

    # Step 5: close.
    Close-Telescope $vs $logPath
}

# --- telescope-prompt-motions --------------------------------------------
# In NORMAL mode the prompt supports vim caret motions (h/l/w/b/e/0/$) over the typed query,
# mirroring the text-input tool windows (covers Run_PromptMotion_*: character, word, line
# start/end, end-word). Each motion logs prompt-motion key=... caret=...
Register-Scenario 'telescope-prompt-motions' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs
    # Type a multi-word query so word motions have something to cross.
    Send-Text 'find my file'
    Assert-NewLogLine $logPath "promptChanged query='find my file'" 'typed query reached prompt'
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 400   # insert -> normal
    Assert-NewLogLine $logPath 'key=Escape mode=insert handled=True' 'Esc switched to normal mode'

    # h/l move by character over 'find my file' (length 12, caret 12 -> 11 -> 12).
    Send-Tap 0x48; Start-Sleep -Milliseconds 200   # h
    Assert-NewLogLine $logPath 'prompt-motion key=H caret=11' 'h moved the prompt caret left to 11'
    Send-Tap 0x4C; Start-Sleep -Milliseconds 200   # l
    Assert-NewLogLine $logPath 'prompt-motion key=L caret=12' 'l moved the prompt caret right to 12'

    # b walks back across word starts: file(8) -> my(5) -> 0.
    Send-Tap 0x42; Start-Sleep -Milliseconds 200   # b
    Assert-NewLogLine $logPath 'prompt-motion key=B caret=8' 'b moved to the start of file'
    Send-Tap 0x42; Start-Sleep -Milliseconds 200   # b
    Assert-NewLogLine $logPath 'prompt-motion key=B caret=5' 'b moved to the start of my'
    Send-Tap 0x42; Start-Sleep -Milliseconds 200   # b
    Assert-NewLogLine $logPath 'prompt-motion key=B caret=0' 'b moved to the start of the line'

    # e -> end of the first word (find -> caret 4); w crosses the word starts.
    Send-Tap 0x45; Start-Sleep -Milliseconds 200   # e
    Assert-NewLogLine $logPath 'prompt-motion key=E caret=4' 'e moved to the end of find'
    Send-Tap 0x57; Start-Sleep -Milliseconds 200   # w
    Assert-NewLogLine $logPath 'prompt-motion key=W caret=5' 'w moved to the start of my'
    Send-Tap 0x57; Start-Sleep -Milliseconds 200   # w
    Assert-NewLogLine $logPath 'prompt-motion key=W caret=8' 'w moved to the start of file'
    Send-Tap 0x57; Start-Sleep -Milliseconds 200   # w
    Assert-NewLogLine $logPath 'prompt-motion key=W caret=12' 'w moved past the last word'

    # 0 -> line start, $ (Shift+4) -> line end.
    Send-Tap 0x30; Start-Sleep -Milliseconds 200   # 0
    Assert-NewLogLine $logPath 'prompt-motion key=D0 caret=0' '0 moved the caret to the line start'
    Send-Shift 0x34; Start-Sleep -Milliseconds 200  # $
    Assert-NewLogLine $logPath 'prompt-motion key=D4 caret=12' '$ moved the caret to the line end'
    Close-Telescope $vs $logPath
}

# --- telescope-preview-motions -------------------------------------------
# The preview pane supports the full vim motion set (h/l/j/k/w/b/e/0/$/g/G) read-only over the
# selected file. We filter to the seeded Motions.cs (deterministic 5-line content) and assert the
# EXACT caret/line for each motion — covering the Run_Preview_* unit-test motions live.
Register-Scenario 'telescope-preview-motions' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs
    Send-Text 'Motions'
    Assert-NewLogLine $logPath "promptChanged query='Motions'" 'typed query reached prompt'
    Assert-NewLogLine $logPath "$($script:PfxTel)results count=1 selected=0" 'filter rendered the single Motions.cs match'
    Assert-NewLogLine $logPath "$($script:PfxTel)preview file=.*Motions\.cs" 'preview shows the Motions.cs match'
    Send-Ctrl 0x4C
    Assert-NewLogLine $logPath 'focus target=Preview' 'Ctrl+L moved focus to the preview'

    # j/k step down/up a line at a time over the 5-line file.
    Send-Tap 0x4A; Start-Sleep -Milliseconds 200   # j
    Assert-NewLogLine $logPath 'preview caret=14 line=2' 'j moved the preview caret to line 2'
    Send-Tap 0x4A; Start-Sleep -Milliseconds 200   # j
    Assert-NewLogLine $logPath 'preview caret=16 line=3' 'j moved the preview caret to line 3'
    Send-Tap 0x4A; Start-Sleep -Milliseconds 200   # j
    Assert-NewLogLine $logPath 'preview caret=35 line=4' 'j moved the preview caret to line 4'
    Send-Tap 0x4A; Start-Sleep -Milliseconds 200   # j
    Assert-NewLogLine $logPath 'preview caret=62 line=5' 'j moved the preview caret to the last line'
    Send-Tap 0x4B; Start-Sleep -Milliseconds 200   # k
    Assert-NewLogLine $logPath 'preview caret=35 line=4' 'k moved the preview caret up to line 4'
    Send-Tap 0x4B; Start-Sleep -Milliseconds 200   # k
    Assert-NewLogLine $logPath 'preview caret=16 line=3' 'k moved the preview caret up to line 3'

    # g -> top (line 1), G (Shift+g) -> bottom (line 5).
    Send-Tap 0x47; Start-Sleep -Milliseconds 200   # g
    Assert-NewLogLine $logPath 'preview caret=0 line=1' 'g moved the preview caret to the top'
    Send-Shift 0x47; Start-Sleep -Milliseconds 200 # G
    Assert-NewLogLine $logPath 'preview caret=63 line=5' 'G moved the preview caret to the last line'

    # Back to line 4 (the 'string beta' line) for the word motions: w/b/e/h/l/0/$.
    Send-Tap 0x47; Start-Sleep -Milliseconds 200   # g -> top
    Send-Tap 0x4A; Start-Sleep -Milliseconds 200   # j -> line 2
    Send-Tap 0x4A; Start-Sleep -Milliseconds 200   # j -> line 3
    Send-Tap 0x4A; Start-Sleep -Milliseconds 200   # j -> line 4
    Send-Tap 0x57; Start-Sleep -Milliseconds 200   # w
    Assert-NewLogLine $logPath 'preview caret=39 line=4' 'w moved to the start of string'
    Send-Tap 0x57; Start-Sleep -Milliseconds 200   # w
    Assert-NewLogLine $logPath 'preview caret=46 line=4' 'w moved to the start of beta'
    Send-Tap 0x45; Start-Sleep -Milliseconds 200   # e
    Assert-NewLogLine $logPath 'preview caret=50 line=4' 'e moved to the end of beta'
    Send-Tap 0x48; Start-Sleep -Milliseconds 200   # h
    Assert-NewLogLine $logPath 'preview caret=49 line=4' 'h moved one char left'
    Send-Tap 0x4C; Start-Sleep -Milliseconds 200   # l
    Assert-NewLogLine $logPath 'preview caret=50 line=4' 'l moved one char right'
    Send-Tap 0x42; Start-Sleep -Milliseconds 200   # b
    Assert-NewLogLine $logPath 'preview caret=46 line=4' 'b moved back to the start of beta'
    Send-Tap 0x30; Start-Sleep -Milliseconds 200   # 0
    Assert-NewLogLine $logPath 'preview caret=35 line=4' '0 moved to the line start'
    Send-Shift 0x34; Start-Sleep -Milliseconds 200  # $
    Assert-NewLogLine $logPath 'preview caret=61 line=4' '$ moved to the line end'
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 200
    Assert-NewLogLine $logPath 'focus target=List' 'Escape returned focus to the list'
    Close-Telescope $vs $logPath
}

# --- telescope-q-close ---------------------------------------------------
# In NORMAL mode both q and Escape close the overlay (Run_KeyHandler_QAndEscapeCloseInNormal).
Register-Scenario 'telescope-q-close' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 400   # insert -> normal
    Assert-NewLogLine $logPath 'key=Escape mode=insert handled=True' 'Esc switched to normal mode'
    Send-Tap 0x51   # q
    Assert-NewLogLine $logPath "$($script:PfxTel)key=Q mode=normal handled=True" 'q was handled in normal mode'
    Assert-NewLogLine $logPath "$($script:PfxTel)overlay closed" 'q closed the overlay'
}

# --- telescope-open-file-normal ------------------------------------------
# Enter selects the current result in NORMAL mode too (Run_KeyHandler_EnterSelectsInInsertAndNormal).
Register-Scenario 'telescope-open-file-normal' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs
    Send-Text 'Program'
    Assert-NewLogLine $logPath "promptChanged query='Program'" 'typed query reached prompt'
    Assert-NewLogLine $logPath "$($script:PfxTel)results count=1 selected=0" 'filter rendered the single Program.cs match'
    Assert-NewLogLine $logPath "$($script:PfxTel)preview file=.*Program\.cs" 'preview shows the Program.cs match'
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 400   # insert -> normal
    Assert-NewLogLine $logPath 'key=Escape mode=insert handled=True' 'Esc switched to normal mode'
    Send-Tap $script:VkEnter; Start-Sleep -Milliseconds 800   # Enter selects in normal mode
    Assert-NewLogLine $logPath "$($script:PfxTel)key=Return mode=normal handled=True" 'Enter (Return) was handled in normal mode'
    Assert-NewLogLine $logPath "$($script:PfxTel)opened file: .*Program\.cs" 'Enter opened Program.cs in normal mode'
}

# --- telescope-open-file-searchbox ----------------------------------------
# In INSERT mode, type a query that filters to EXACTLY ONE file, WAIT for the filter to settle
# (results count=1 selected=0), then Enter opens that exact filtered candidate. Distinct from
# telescope-open-file (which presses Enter without first confirming the settle line before opening).
Register-Scenario 'telescope-open-file-searchbox' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs
    Send-Text 'Program'                                          # unique match in the scratch solution
    Assert-NewLogLine $logPath "promptChanged query='Program'" 'typed query reached prompt'
    Assert-NewLogLine $logPath "$($script:PfxTel)results count=1 selected=0" 'filter settled to the single Program.cs match'
    Assert-NewLogLine $logPath "$($script:PfxTel)preview file=.*Program\.cs" 'preview shows the filtered Program.cs match'
    Send-Tap $script:VkEnter; Start-Sleep -Milliseconds 800     # Enter (INSERT mode) opens the match
    Assert-NewLogLine $logPath "$($script:PfxTel)opened file: .*Program\.cs" 'Enter opened Program.cs from the search box'
    Close-Telescope $vs $logPath
}

# --- telescope-open-file-navigation ----------------------------------------
# Type a query matching >=2 files, Esc to NORMAL mode, move selection with j to index 1, then Enter
# opens the MOVED-TO row (proves Enter opens the selected row, not just index 0) — distinct from
# telescope-open-file-normal (opens index 0 without moving). The "Service" query matches the seeded
# Services/AuthService.cs + Service.cs (fzf --no-sort preserves DTE order, folder files first), so
# index 1 is the top-level Service.cs.
Register-Scenario 'telescope-open-file-navigation' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs
    Send-Text 'Service'
    Assert-NewLogLine $logPath "promptChanged query='Service'" 'typed query reached prompt'
    Assert-NewLogLine $logPath "$($script:PfxTel)results count=2 selected=0" 'Service matched exactly two files'
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 400   # insert -> normal
    Assert-NewLogLine $logPath 'key=Escape mode=insert handled=True' 'Esc switched to normal mode'
    Send-Tap 0x4A; Start-Sleep -Milliseconds 200              # j -> index 1
    Assert-NewLogLine $logPath "$($script:PfxTel)results count=2 selected=1" 'j moved selection to index 1'
    Assert-NewLogLine $logPath "$($script:PfxTel)preview file=.*[\\/]Service\.cs" 'preview shows the index-1 file (Service.cs)'
    Send-Tap $script:VkEnter; Start-Sleep -Milliseconds 800   # Enter selects the moved-to row
    Assert-NewLogLine $logPath "$($script:PfxTel)opened file: .*[\\/]Service\.cs" 'Enter opened the moved-to row (Service.cs)'
    Close-Telescope $vs $logPath
}

# --- telescope-no-selection ----------------------------------------------
# Movement on an EMPTY result list is a no-op that keeps the selection at 0
# (Run_KeyHandler_NoSelectionWithoutResults).
Register-Scenario 'telescope-no-selection' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-Telescope $vs $logPath
    Assert-OverlayFocused $vs
    Send-Text 'zzzznomatch'
    Assert-NewLogLine $logPath "promptChanged query='zzzznomatch'" 'typed query reached prompt'
    Assert-NewLogLine $logPath "$($script:PfxTel)results count=0 selected=0" 'filter returned no results'
    Send-Tap $script:VkEscape; Start-Sleep -Milliseconds 400   # insert -> normal
    Send-Tap 0x4A; Start-Sleep -Milliseconds 200              # j on the empty list
    Assert-NewLogLine $logPath 'key=J mode=normal handled=True' 'j was handled in normal mode'
    Assert-NewLogLine $logPath "$($script:PfxTel)results count=0 selected=0" 'selection stayed at 0 on an empty list'
    Close-Telescope $vs $logPath
}

# --- seed-reset -----------------------------------------------------------
# Filesystem-only (no keystrokes, no [Telescope]/[NeoVisual] log assertions): proves the scratch
# seeding ALWAYS resets (stale edits removed) and writes UNIFORM line endings (no mixed CRLF/LF,
# which would make VS show the "normalize line endings?" modal and steal focus). Uses a TEMP dir,
# never the live $scratch the running VS has open, so this never disturbs the instance.
Register-Scenario 'seed-reset' {
    param($vs, $logPath)
    $testDir = Join-Path $env:TEMP ("scratch_seed_test_" + $PID)
    try {
        Reset-ScratchSolution $testDir
        Assert-SeedConsistent $testDir          # uniform EOL + canonical content after a reset

        # Mutate a seeded file, then reset again and prove the stale edit is removed.
        $beta = Join-Path $testDir 'Probe\Beta.cs'
        if (-not (Test-Path $beta)) { throw "expected seeded file missing: $beta" }
        [System.IO.File]::AppendAllText($beta, "// STALE-MARKER`r`n")
        if (-not (Select-String -Path $beta -Pattern 'STALE-MARKER' -Quiet)) {
            throw "failed to append stale marker to $beta"
        }

        Reset-ScratchSolution $testDir
        if (Select-String -Path $beta -Pattern 'STALE-MARKER' -Quiet) {
            throw "reset did not remove stale edit: $beta still contains STALE-MARKER"
        }
        Assert-SeedConsistent $testDir          # still uniform + canonical after the second reset
        Write-Pass 'seed-reset: reset is idempotent and seed EOL is uniform'
    } finally {
        if (Test-Path $testDir) { Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

# --- seed-leak ------------------------------------------------------------
# Filesystem-only leak guard: NO scenario may WRITE into a seeded file. The bootstrap generates
# an expected-result copy of every seeded file (log/seed-expected/) right after the reseed; a
# scenario that INTENTIONALLY writes a seed refreshes that file's expected copy (Update-SeedExpected)
# once it has validated the write; this scenario byte-compares the whole seed tree to the expected
# tree and fails on any added / removed / modified seeded file. There is NO ignorelist. Put it LAST
# so the whole run's writes are checked. Skips gracefully under -NoBootstrap (no expected tree).
Register-Scenario 'seed-leak' {
    param($vs, $logPath)
    $expected = Join-Path $logDir 'seed-expected'
    $scratch = if ($env:NEOVISUAL_TEST_SOLUTION) { Split-Path $env:NEOVISUAL_TEST_SOLUTION } else { Join-Path $env:TEMP 'telescope_scratch' }
    Assert-NoSeedLeak $scratch $expected
}

# ---------------------------------------------------------------------------
# Runner: list, filter, boot, run
# ---------------------------------------------------------------------------

if ($List) {
    Write-Host 'Available scenarios:'
    foreach ($name in $script:Scenarios.Keys) { Write-Host "  $name" }
    exit 0
}

$allNames = @($script:Scenarios.Keys)
$selected = if ($Tests.Count -gt 0) { $Tests | Where-Object { $allNames -contains $_ } } else { $allNames }
if ($selected.Count -lt $Tests.Count) {
    $unknown = $Tests | Where-Object { $allNames -notcontains $_ }
    Write-Fail "Unknown scenario(s): $($unknown -join ', ')"
    Write-Host "Valid: $($allNames -join ', ')"
    exit 1
}

Write-Step "E2E scenarios: $($selected -join ', ')"

# --- key injection helper (keybd_event, like the repo's KeyInjection) -----
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class KbInject
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    private const uint KEYEVENTF_KEYUP = 0x0002;
    public static void TapVk(ushort vk)
    {
        keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
        keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
'@ -Language CSharp
Add-Type -Namespace Win32 -Name Fg -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId); [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);'
Add-Type -Namespace Win32 -Name Kbd -MemberDefinition '[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);'

# ---------------------------------------------------------------------------
# Bootstrap: scratch solution with multiple source files, deploy, launch exp
# ---------------------------------------------------------------------------
# M-C1: refresh the tools/ hash up front so check-doc-refs.ps1 can enforce its presence and the
# hub's VERIFY `tools/`-changed flag is always computed against a fresh hash.
Write-ToolsHash

if ($NoBootstrap) {
    # Reuse mode (M-M5): the instance is ALREADY booted — discover it instead of killing, reseeding,
    # and re-spawning. Retries/batches against the same instance reuse this boot, so a -Tests
    # re-run no longer does a full VS reboot. The scratch is NOT reset here (the running VS has it
    # open); run seed-reset if you need a clean reseed.
    Write-Step 'Reusing an already-booted instance (-NoBootstrap)'
    $vsProc = Get-Process devenv -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowTitle -match 'Experimental' -and $_.MainWindowHandle -ne 0 } |
        Select-Object -First 1
    $mainVs = Get-Process devenv -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowTitle -match 'MyExtension' } |
        Select-Object -First 1
    if (-not $vsProc) { Write-Fail 'No running Experimental instance found (boot without -NoBootstrap first)'; exit 1 }
    Add-SpawnedVs $vsProc
    if ($mainVs) { Add-SpawnedVs $mainVs }
    # Point at the newest existing exp log (the running instance's log), else the fresh run path.
    $logPath = Get-ChildItem $logDir -Filter '*-neovisual-exp.log' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $logPath) { Write-Fail 'No existing exp log found to reuse'; exit 1 }
    $runIndex = if ($logPath -match '(\d+)-neovisual') { [int]$Matches[1] } else { 1 }
    $debugLogPath = Join-Path $logDir "$runIndex-neovisual-main.log"
    # Reuse mode: no reseed happened, so seed-leak has no clean baseline in this process. Snapshot
    # the current scratch (best-effort) so seed-leak still guards against writes from THIS run.
    $reuseScratch = Join-Path $env:TEMP 'telescope_scratch'
    if (Test-Path $reuseScratch) { Write-SeedExpected $reuseScratch (Join-Path $logDir 'seed-expected') }
    Write-Pass "reusing Experimental instance (PID $($vsProc.Id)), log: $(Split-Path $logPath -Leaf)"
} else {
# HARD ORDERING REQUIREMENT: stop ANY prior harness VS BEFORE the scratch reset below, so the
# delete cannot fight a live solution or be blocked by a locked scratch dir. Scoped (M-M5) to
# harness-looking VS only — never the user's other devenv instances.
Stop-HarnessVs
Start-Sleep -Seconds 3
Write-Step 'Ensure scratch solution (multiple source files)'
$scratch = Join-Path $env:TEMP 'telescope_scratch'
$slnPath = Join-Path $scratch 'TelescopeTest.sln'
Reset-ScratchSolution $scratch
# Bootstrap self-check: must pass once the seed is uniform + canonical. Throws -> exit 1.
Assert-SeedConsistent $scratch
# Seed-leak baseline: snapshot every seeded file NOW (post-reseed, pre-run) so the seed-leak
# scenario can prove no scenario wrote into a seed during the run.
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
Write-SeedExpected $scratch (Join-Path $logDir 'seed-expected')
Write-Pass "solution ready: $slnPath"

# Run index: auto-incrementing integer from existing logs.
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

# Resolve VS install root.
$candidates = @('C:\Program Files\Microsoft Visual Studio\18\Community','C:\Program Files\Microsoft Visual Studio\2022\Community')
$vsRoot = $candidates | Where-Object { Test-Path (Join-Path $_ 'Common7\IDE\devenv.exe') } | Select-Object -First 1
if (-not $vsRoot) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) { $vsRoot = (& $vswhere -products '*' -property installationPath | Select-Object -First 1) }
}
if (-not $vsRoot) { throw 'No Visual Studio installation found.' }
$devenv = Join-Path $vsRoot 'Common7\IDE\devenv.exe'
Write-Info "VS root: $vsRoot"

# Env for the extension + harness log wiring.
# M-N5: Process scope ONLY — the main VS is spawned by this script (inherits $env:), and
# Debug.Start's exp instance inherits from main VS. USER scope is unnecessary and persists across
# sessions (a silent machine mutation); 'Process' scopes the vars to this run and its children.
[Environment]::SetEnvironmentVariable('NEOVISUAL_TEST_SOLUTION', $slnPath, 'Process')
$env:NEOVISUAL_TEST_SOLUTION = $slnPath
[Environment]::SetEnvironmentVariable('NEOVISUAL_LOG_DIR', $logDir, 'Process')
$env:NEOVISUAL_LOG_DIR = $logDir
$env:NEOVISUAL_LOG_INDEX = $runIndex

# Fresh main VS that carries the env vars into the experimental instance it spawns.
Write-Info 'opening main VS with the solution...'
Start-Process $devenv -ArgumentList "`"$(Join-Path $root 'MyExtension.slnx')`"" -ErrorAction Stop
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$mainVs = $null
while ($sw.Elapsed.TotalSeconds -lt 60) {
    $mainVs = Get-Process devenv -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match 'MyExtension' } | Select-Object -First 1
    if ($mainVs) { break }
    Start-Sleep -Milliseconds 500
}
if (-not $mainVs) { Write-Fail 'Main VS did not open'; exit 1 }
Add-SpawnedVs $mainVs
Write-Pass "main VS open (PID $($mainVs.Id))"
Start-Sleep -Seconds 10

# Stale experimental instance from a prior run (main VS deploys a fresh one below): save then kill,
# so its leak evidence survives and it is never left with unsaved changes.
$staleExp = @(Get-Process devenv -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match 'Experimental' })
foreach ($p in $staleExp) { Save-AllDocuments $p.Id }
foreach ($p in $staleExp) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
if (Test-Path $logPath) { Remove-Item $logPath -Force }

# Deploy via VS's own Debug.Start (guaranteed latest code), then wait for the exp instance.
& (Join-Path $PSScriptRoot 'dte-command.ps1') -DevenvPid $mainVs.Id -Command 'Debug.Start' | Out-Null
Write-Pass 'Debug.Start issued'
$vsProc = $null
$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt 90) {
    $vsProc = Get-Process devenv -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match 'Experimental' -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($vsProc) { break }
    Start-Sleep -Milliseconds 400
}
if (-not $vsProc) { Write-Fail 'Experimental instance did not appear'; exit 1 }
Add-SpawnedVs $vsProc
Write-Pass "Experimental instance found (PID $($vsProc.Id))"

# Wait for the extension to be live: hook installed + solution auto-opened.
if (-not (Wait-LogContains $logPath "$($script:PfxHook)installed" 90000)) { Write-Fail 'keyboard hook not installed'; exit 1 }
if (-not (Wait-LogContains $logPath "$($script:PfxMyExt)auto-opened solution" 30000)) { Write-Fail 'solution not auto-opened'; exit 1 }
Write-Pass 'extension live (hook + solution open)'
}

# ---------------------------------------------------------------------------
# Run the selected scenarios against the live instance
# ---------------------------------------------------------------------------
$failures = @()
foreach ($name in $selected) {
    Write-Step "Scenario: $name"
    $ok = $false
    try {
        & $script:Scenarios[$name] $vsProc $logPath
        $ok = $true
    } catch {
        $failures += "$name : $($_.Exception.Message)"
    }
    if ($ok) { Write-Pass "scenario '$name' passed" } else { Write-Fail "scenario '$name' FAILED" }
}

Write-Host ''
if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Fail $f }
    Write-Host "RESULT: FAIL ($($failures.Count) failed)" -ForegroundColor Red
    if (-not $KeepVs) { Stop-SpawnedVs }
    exit 1
}
Write-Host "RESULT: PASS (all $($selected.Count) scenario(s))" -ForegroundColor Green
if (-not $KeepVs) { Stop-SpawnedVs }
exit 0
