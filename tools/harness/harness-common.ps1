# tools/harness/harness-common.ps1 — shared helper module for the MyExtension e2e harness.
#
# Dot-sourced by tools/harness/test-e2e.ps1, tools/harness/iterate-telescope.ps1, and tools/harness/dte-command.ps1.
# Holds the duplicated helper set (prefix vars, Write-*, key injection, log waits, foreground,
# VS-root resolution) so the three scripts share one byte-compatible implementation.

# Per-scenario log baseline: assertions search the whole post-baseline window (never advance), so
# an assert may re-match a line that an earlier helper (e.g. Open-Telescope) already confirmed.
$script:LogBaseline = 0

# F39: incremental tail-read state. $script:LogCache holds every line read so far (from byte 0), so
# the wait helpers search only the appended tail (O(n) total) instead of re-reading the whole file
# per poll (O(n^2)). $script:LogReadBytes is the byte offset of the last read. The fixed baseline is
# NEVER advanced: a per-call cursor advances on READ only, never on match.
$script:LogReadBytes = 0
$script:LogCache = [System.Collections.Generic.List[string]]::new()

$script:VkEscape = 0x1B
$script:VkSpace = 0x20
$script:VkEnter = 0x0D
$script:VkTab = 0x09
$script:VkF = 0x46
$script:VkT = 0x54
$script:VkD = 0x44
$script:VkR = 0x52
$script:VkG = 0x47
$script:VkI = 0x49
$script:VkJ = 0x4A
$script:VkK = 0x4B
$script:VkL = 0x4C
$script:VkH = 0x48
$script:VkO = 0x4F
$script:VkA = 0x41
$script:VkM = 0x4D
$script:VkW = 0x57
$script:VkE = 0x45

# Single source for log prefixes (regex-escaped) — keep in sync with
# Telescope/Logging/DiagnosticLog.cs; pinned by unit test Run_LogPrefixes_Pinned.
$script:PfxNeo = '\[NeoVisual\] '
$script:PfxTel = '\[Telescope\] '
$script:PfxHook = '\[Hook\] '
$script:PfxMyExt = '\[MyExtension\] '

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Info($msg) { Write-Host "    $msg" }
function Write-Pass($msg) { Write-Host "    PASS: $msg" -ForegroundColor Green }
function Write-Fail($msg) { Write-Host "    FAIL: $msg" -ForegroundColor Red }

function Assert-Budget([System.Diagnostics.Stopwatch]$Stopwatch, [int]$TimeoutSec) {
    # M37: checkpoint stopwatch — throws when the elapsed time exceeds the budget. Parameterized so
    # it is directly unit-testable (iterate-telescope.ps1 keeps its local no-arg Assert-Budget,
    # which shadows this one — harmless).
    if ($Stopwatch.Elapsed.TotalSeconds -gt $TimeoutSec) { throw "Timed out after $TimeoutSec s" }
}

function Update-LogCache([string]$logPath) {
    # F39: read ONLY the appended tail (bytes after $script:LogReadBytes) into the cumulative
    # $script:LogCache. O(n) total across polls — never re-reads the whole file. The byte offset is
    # always at a line boundary (recorded after reading complete lines to EOF), so UTF-8 multi-byte
    # characters are never split. A truncated/recreated file restarts from byte 0.
    if (-not (Test-Path $logPath)) { return }
    $fs = [System.IO.File]::Open($logPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        if ($fs.Length -lt $script:LogReadBytes) {
            $script:LogReadBytes = 0
            $script:LogCache.Clear()
        }
        if ($fs.Length -le $script:LogReadBytes) { return }
        $fs.Seek($script:LogReadBytes, [System.IO.SeekOrigin]::Begin) | Out-Null
        $reader = [System.IO.StreamReader]::new($fs, [System.Text.Encoding]::UTF8, $true, 1024, $true)
        try {
            while (-not $reader.EndOfStream) {
                $line = $reader.ReadLine()
                if ($null -ne $line) { $script:LogCache.Add($line) }
            }
        } finally {
            $reader.Dispose()
        }
        $script:LogReadBytes = $fs.Position
    } finally {
        $fs.Dispose()
    }
}

function Reset-LogBaseline([string]$logPath) {
    Update-LogCache $logPath
    $script:LogBaseline = $script:LogCache.Count
}

function Get-LogCacheIndex([string]$logPath) {
    # m26/m28: cache-side snapshot of the log's current line count. Callers pass this to
    # Wait-NewLogLineAfter as a cache offset — a fresh `Get-Content` count can exceed the cache and
    # make the wait time out, or lag it and match stale lines.
    Update-LogCache $logPath
    return $script:LogCache.Count
}

function Send-Tap([int]$vk) { [KbInject]::TapVk([uint16]$vk) }
function Send-Shift([int]$vk) {
    # Shift+<key> chord (e.g. G = move to last) via keybd_event.
    [Win32.Kbd]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero)
    [KbInject]::TapVk([uint16]$vk)
    [Win32.Kbd]::keybd_event(0x10, 0, 2, [UIntPtr]::Zero)  # KEYEVENTF_KEYUP
}
function Send-Text([string]$text) {
    # F41: map each char to the correct VK. Letters/digits use the char code (uppercase letter code
    # == VK); punctuation must map to its base key + shift (e.g. '!' is Shift+1, NOT char code 0x21
    # which is VK_PRIOR/PageUp). Each entry is @(vk, needsShift).
    # M3: an UPPERCASE letter (e.g. 'P' in 'Program') also needs Shift — the VK is the uppercase
    # code, but without Shift the OS types the lowercase char, so 'Program' would type 'program'.
    $punct = @{
        '!' = @(0x31, $true);  '@' = @(0x32, $true);  '#' = @(0x33, $true);  '$' = @(0x34, $true)
        '%' = @(0x35, $true);  '^' = @(0x36, $true);  '&' = @(0x37, $true);  '*' = @(0x38, $true)
        '(' = @(0x39, $true);  ')' = @(0x30, $true);  '_' = @(0xBD, $true);  '+' = @(0xBB, $true)
        '{' = @(0xDB, $true);  '}' = @(0xDD, $true);  '|' = @(0xDC, $true);  ':' = @(0xBA, $true)
        '"' = @(0xDE, $true);  '<' = @(0xBC, $true);  '>' = @(0xBE, $true);  '?' = @(0xBF, $true)
        '~' = @(0xC0, $true)
        '-' = @(0xBD, $false); '=' = @(0xBB, $false); '[' = @(0xDB, $false); ']' = @(0xDD, $false)
        '\' = @(0xDC, $false); ';' = @(0xBA, $false); "'" = @(0xDE, $false); ',' = @(0xBC, $false)
        '.' = @(0xBE, $false); '/' = @(0xBF, $false); '`' = @(0xC0, $false)
    }
    foreach ($ch in $text.ToCharArray()) {
        $vk = [int][char]::ToUpper($ch)
        $shift = $false
        if ($punct.ContainsKey([string]$ch)) {
            $vk = $punct[[string]$ch][0]
            $shift = $punct[[string]$ch][1]
        } elseif ([char]::IsUpper($ch)) {
            $shift = $true
        }
        if ($shift) {
            [Win32.Kbd]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero)   # Shift down
            [KbInject]::TapVk([uint16]$vk)
            [Win32.Kbd]::keybd_event(0x10, 0, 2, [UIntPtr]::Zero)   # Shift up
        } else {
            [KbInject]::TapVk([uint16]$vk)
        }
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

function Wait-LogLine([string]$logPath, [string]$pattern, [int]$fromIndex, [int]$pollMs = 300, [int]$maxMs = 20000) {
    # The single core wait (M28): searches $script:LogCache from -FromIndex (a fixed baseline/cursor
    # that is NEVER advanced on match — an assert may re-confirm a line another helper already saw),
    # polls every -PollMs, bounded by -MaxMs. Returns true when the pattern matches.
    # F39: reads only the appended tail via Update-LogCache; the per-call cursor starts at -FromIndex
    # and advances on READ only (never on match), so the whole window is searched cumulatively in
    # O(n) total.
    # m65: matches PER-LINE over the cache (never joins the tail and -match'es it), so a pattern can
    # never match across a line boundary (e.g. 'foo\s+bar' with foo and bar on different lines).
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $searchedTo = $fromIndex
    while ($sw.Elapsed.TotalMilliseconds -lt $maxMs) {
        Update-LogCache $logPath
        if ($script:LogCache.Count -gt $searchedTo) {
            for ($i = $searchedTo; $i -lt $script:LogCache.Count; $i++) {
                if ($script:LogCache[$i] -match $pattern) { return $true }
            }
            $searchedTo = $script:LogCache.Count
        }
        Start-Sleep -Milliseconds $pollMs
    }
    return $false
}

function Wait-NewLogLine([string]$logPath, [string]$pattern, [int]$maxMs = 20000) {
    # Searches lines appended after the per-scenario baseline (does NOT advance — an assert may
    # re-confirm a line another helper already saw). Returns true when the pattern matches.
    return Wait-LogLine -LogPath $logPath -Pattern $pattern -FromIndex $script:LogBaseline -PollMs 300 -MaxMs $maxMs
}

function Wait-NewLogLineAfter([string]$logPath, [int]$fromIndex, [string]$pattern, [int]$maxMs = 3000) {
    # POSITIVE bounded wait over lines appended AFTER a caller-supplied absolute line index (a
    # snapshot taken immediately BEFORE the key under test). Unlike Wait-NewLogLine it excludes
    # earlier lines, so it can attribute a new diagnostic (e.g. an editor-focus vim-mode=/editor-view
    # line) to the key just pressed. Positive wait, NOT an absence assertion.
    return Wait-LogLine -LogPath $logPath -Pattern $pattern -FromIndex $fromIndex -PollMs 200 -MaxMs $maxMs
}

function Wait-LogContains([string]$logPath, [string]$pattern, [int]$maxMs = 20000) {
    # Whole-file matcher (bootstrap/startup waits); does not use the scenario cursor.
    return Wait-LogLine -LogPath $logPath -Pattern $pattern -FromIndex 0 -PollMs 300 -MaxMs $maxMs
}

function Assert-NewLogLine([string]$logPath, [string]$pattern, [string]$what, [int]$maxMs = 20000) {
    if (-not (Wait-NewLogLine $logPath $pattern $maxMs)) {
        throw "never saw: $what (pattern: $pattern)"
    }
}

function Assert-NewLogLineAfter([string]$logPath, [int]$fromIndex, [string]$pattern, [string]$what, [int]$maxMs = 3000) {
    # R5: assertion over lines appended AFTER a caller-supplied absolute log index (a snapshot taken
    # immediately BEFORE the key under test), so it can only be satisfied by a NEW diagnostic emitted
    # by that key — not by an earlier line inside the fixed per-scenario baseline window.
    if (-not (Wait-NewLogLineAfter $logPath $fromIndex $pattern $maxMs)) {
        throw "never saw: $what (pattern: $pattern)"
    }
}

function Assert-NoEnterStorm([string]$logPath, [string]$what) {
    # Fail-fast (F1): the Enter/o -> OpenSelected() re-injection storm fires ~30 'solution-explorer
    # open' lines in ~100ms. A legitimate walk presses Enter/o at most once per loop iteration
    # (<=8), so >10 post-baseline open lines means the storm is present. Count WITHOUT advancing the
    # baseline (same fixed-baseline discipline as Wait-NewLogLine).
    if (-not (Test-Path $logPath)) { return }
    Update-LogCache $logPath
    $openCount = 0
    for ($i = $script:LogBaseline; $i -lt $script:LogCache.Count; $i++) {
        if ($script:LogCache[$i] -match "$($script:PfxNeo)solution-explorer open") { $openCount++ }
    }
    if ($openCount -gt 10) {
        throw "Enter-storm: $openCount 'solution-explorer open' lines post-baseline (bound 10) during $what"
    }
}

function Resolve-VsRoot {
    # Resolve the VS install root. Prefer the well-known 18/Community path (also works while VS is
    # updating and vswhere returns nothing), falling back to vswhere.
    # m60: also consider Professional/Enterprise/Preview editions (not just Community) before the
    # vswhere fallback, so a non-Community install is found without vswhere.
    $candidates = @(
        'C:\Program Files\Microsoft Visual Studio\18\Community'
        'C:\Program Files\Microsoft Visual Studio\18\Professional'
        'C:\Program Files\Microsoft Visual Studio\18\Enterprise'
        'C:\Program Files\Microsoft Visual Studio\18\Preview'
        'C:\Program Files\Microsoft Visual Studio\2022\Community'
        'C:\Program Files\Microsoft Visual Studio\2022\Professional'
        'C:\Program Files\Microsoft Visual Studio\2022\Enterprise'
        'C:\Program Files\Microsoft Visual Studio\2022\Preview'
    )
    $vsRoot = $candidates | Where-Object { Test-Path (Join-Path $_ 'Common7\IDE\devenv.exe') } | Select-Object -First 1
    if (-not $vsRoot) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path $vswhere) {
            $vsRoot = (& $vswhere -products '*' -property installationPath | Select-Object -First 1)
        }
    }
    if (-not $vsRoot) { throw 'No Visual Studio installation found.' }
    return $vsRoot
}

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
